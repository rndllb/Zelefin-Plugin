using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Zelefin.Api;
using Jellyfin.Plugin.Zelefin.Configuration;
using Jellyfin.Plugin.Zelefin.Push;
using Jellyfin.Plugin.Zelefin.Storage;
using Xunit;

namespace Jellyfin.Plugin.Zelefin.Tests;

public class ClientConfigTests
{
    [Fact]
    public void From_omits_apns_secrets_and_normalizes_seerr_url()
    {
        var config = new PluginConfiguration
        {
            SeerrUrl = "seerr.example.com/",
            AutoSkipIntro = true,
            AutoSkipCredits = false,
            LockAutoSkip = true,
            ApnsKeyId = "ABC",
            ApnsTeamId = "TEAM",
            ApnsBundleId = "app.zelefin.client",
            ApnsPrivateKey = "-----BEGIN PRIVATE KEY-----\nMHsCAQEE\n-----END PRIVATE KEY-----"
        };

        var client = ClientConfig.From(config);

        Assert.Equal("https://seerr.example.com", client.SeerrUrl);
        Assert.True(client.AutoSkipIntro);
        Assert.False(client.AutoSkipCredits);
        Assert.True(client.LockAutoSkip);
        Assert.True(client.NotificationsReady);
        Assert.Equal("", client.SeerrApiKey);
        Assert.False(client.SeerrSignInReady);
        var json = JsonSerializer.Serialize(client);
        Assert.DoesNotContain("PRIVATE KEY", json);
        Assert.DoesNotContain("Apns", json);
    }

    [Fact]
    public void NotificationsReady_is_true_before_an_apple_key_is_pasted()
    {
        var client = ClientConfig.From(new PluginConfiguration());
        Assert.True(client.NotificationsReady);
        Assert.False(new PluginConfiguration().IsApnsConfigured);
    }

    [Fact]
    public void Default_config_sends_through_the_publisher_relay()
    {
        var config = new PluginConfiguration();
        Assert.False(config.IsApnsConfigured);
        Assert.True(config.HasPushRelay);
        Assert.True(config.CanSendPush);
        Assert.Equal(PluginConfiguration.DefaultPushRelayUrl, config.PushRelayUrl);
        Assert.Null(config.LocalApnsCredentials());
    }

    [Fact]
    public void Clearing_the_relay_url_disables_push_without_a_local_key()
    {
        var config = new PluginConfiguration { PushRelayUrl = " " };
        Assert.False(config.HasPushRelay);
        Assert.False(config.CanSendPush);
    }

    [Fact]
    public void Local_apns_still_works_when_a_key_is_already_saved()
    {
        var config = new PluginConfiguration
        {
            PushRelayUrl = "",
            ApnsKeyId = "KEYID",
            ApnsTeamId = "TEAMID",
            ApnsBundleId = "app.zelefin.client",
            ApnsPrivateKey = "-----BEGIN PRIVATE KEY-----\nMHsCAQEE\n-----END PRIVATE KEY-----"
        };
        Assert.True(config.IsApnsConfigured);
        Assert.True(config.CanSendPush);
        Assert.NotNull(config.LocalApnsCredentials());
    }

    [Fact]
    public void Relay_body_maps_tokens_and_message()
    {
        var body = PushRelayClient.Body(
            ["tok-a", "tok-b"],
            ["fcm-a"],
            new PushMessage
            {
                Title = "New episode",
                Subtitle = "Andor",
                Body = "S1:E1",
                ItemId = "abc",
                SeriesId = "series",
                Type = "Episode"
            });
        Assert.Equal(["tok-a", "tok-b"], body.Tokens);
        Assert.Equal(["fcm-a"], body.FcmTokens);
        Assert.Equal("New episode", body.Title);
        Assert.Equal("Andor", body.Subtitle);
        Assert.Equal("S1:E1", body.Body);
        Assert.Equal("abc", body.ItemId);
        Assert.Equal("series", body.SeriesId);
        Assert.Equal("Episode", body.Type);
    }

    [Fact]
    public void From_includes_seerr_api_key_when_set()
    {
        var client = ClientConfig.From(new PluginConfiguration
        {
            SeerrUrl = "https://seerr.example.com",
            SeerrApiKey = " seerr-secret "
        });
        Assert.True(client.SeerrSignInReady);
        Assert.Equal("seerr-secret", client.SeerrApiKey);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("https://requests.local", "https://requests.local")]
    [InlineData("http://10.0.0.8:5055/", "http://10.0.0.8:5055")]
    public void NormalizeUrl(string input, string expected)
    {
        Assert.Equal(expected, PluginConfiguration.NormalizeUrl(input));
    }

    [Fact]
    public void ParsedLibraryIds_splits_and_dedupes()
    {
        var config = new PluginConfiguration { ItemAddedLibraryIds = "aaa, BBB,aaa, " };
        Assert.Equal(["aaa", "BBB"], config.ParsedLibraryIds());
    }
}

public class NotificationTargetTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DeviceRecord AdaPhone = new()
    {
        DeviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        UserId = Ada,
        Token = "ada-token"
    };
    private static readonly DeviceRecord BobPhone = new()
    {
        DeviceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        UserId = Bob,
        Token = "bob-token"
    };

    [Fact]
    public void Empty_request_targets_everyone()
    {
        var targets = NotificationTargets.Resolve(
            new PushNotificationRequest { Title = "Hi", Body = "There" },
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada },
            _ => null);
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void UserId_targets_that_user()
    {
        var targets = NotificationTargets.Resolve(
            new PushNotificationRequest { Title = "Hi", Body = "There", UserId = Bob },
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada },
            _ => null);
        Assert.Equal([BobPhone.DeviceId], targets.Select(t => t.DeviceId).ToArray());
    }

    [Fact]
    public void Username_resolves_through_lookup()
    {
        var targets = NotificationTargets.Resolve(
            new PushNotificationRequest { Title = "Hi", Body = "There", Username = "bob" },
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada },
            name => name == "bob" ? Bob : null);
        Assert.Single(targets);
        Assert.Equal(Bob, targets[0].UserId);
    }

    [Fact]
    public void Admin_flag_adds_admins()
    {
        var targets = NotificationTargets.Resolve(
            new PushNotificationRequest { Title = "Hi", Body = "There", UserId = Bob, IsAdmin = true },
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada },
            _ => null);
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void Admin_only_when_no_user_and_isAdmin()
    {
        var targets = NotificationTargets.Resolve(
            new PushNotificationRequest { Title = "Hi", Body = "There", IsAdmin = true },
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada },
            _ => null);
        Assert.Equal([AdaPhone.DeviceId], targets.Select(t => t.DeviceId).ToArray());
    }

    [Fact]
    public void ForAdmins_skips_the_acting_user()
    {
        var targets = NotificationTargets.ForAdmins(
            [AdaPhone, BobPhone],
            new HashSet<Guid> { Ada, Bob },
            [Bob]);
        Assert.Equal([Ada], targets.Select(t => t.UserId).ToArray());
    }
}

public class MediaCopyTests
{
    [Fact]
    public void MovieAdded_strips_duplicate_year()
    {
        var message = MediaCopy.MovieAdded("Dune (2021)", 2021, Guid.Parse("33333333-3333-3333-3333-333333333333"));
        Assert.Equal("Dune (2021) added", message.Title);
        Assert.Equal("Watch now", message.Body);
        Assert.Equal("Movie", message.Type);
        Assert.Equal("33333333333333333333333333333333", message.ItemId);
    }

    [Fact]
    public void Single_episode_deep_links_the_episode()
    {
        var batch = new EpisodeBatch { SeasonId = Guid.NewGuid() };
        batch.Add(new EpisodeCapture(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Andor",
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            1,
            3,
            "Reckoning"));
        var message = MediaCopy.EpisodesAdded(batch);
        Assert.Equal("New episode", message.Title);
        Assert.Contains("S1:E3", message.Body);
        Assert.Equal("44444444444444444444444444444444", message.ItemId);
        Assert.Equal("Episode", message.Type);
    }

    [Fact]
    public void Many_episodes_group_into_one_notification()
    {
        var batch = new EpisodeBatch { SeasonId = Guid.NewGuid() };
        batch.Add(new EpisodeCapture(Guid.NewGuid(), "Andor", Guid.NewGuid(), 2, 1, "One"));
        batch.Add(new EpisodeCapture(Guid.NewGuid(), "Andor", Guid.NewGuid(), 2, 2, "Two"));
        var message = MediaCopy.EpisodesAdded(batch);
        Assert.Equal("New episodes", message.Title);
        Assert.Contains("2 episodes of Andor", message.Body);
        Assert.Equal("Season", message.Type);
        Assert.Null(message.ItemId);
    }

    [Fact]
    public void Invalid_webhook_payloads_are_dropped()
    {
        Assert.False(PushMessage.IsValid(new PushNotificationRequest()));
        Assert.False(PushMessage.IsValid(new PushNotificationRequest { Title = "Only title" }));
        Assert.True(PushMessage.IsValid(new PushNotificationRequest { Body = "Body only" }));
        Assert.True(PushMessage.IsValid(new PushNotificationRequest { Title = "Hi", Body = "There" }));
    }
}

public class ApnsJwtTests
{
    [Fact]
    public void Create_signs_es256_jwt()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();
        var jwt = ApnsJwt.Create("KEYID1", "TEAMID1", pem, 1_700_000_000);
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var header = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        Assert.Contains("\"kid\":\"KEYID1\"", header);
        Assert.Contains("ES256", header);
        var payload = Encoding.UTF8.GetString(FromBase64Url(parts[1]));
        Assert.Contains("TEAMID1", payload);
        Assert.True(ecdsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), FromBase64Url(parts[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Payload_includes_aps_alert_and_item_id()
    {
        var json = ApnsClient.Payload(new PushMessage
        {
            Title = "New episode",
            Subtitle = "Andor",
            Body = "S1:E1",
            ItemId = "abc",
            Type = "Episode"
        });
        Assert.Contains("\"title\":\"New episode\"", json);
        Assert.Contains("\"subtitle\":\"Andor\"", json);
        Assert.Contains("\"itemId\":\"abc\"", json);
        Assert.Contains("\"sound\":\"default\"", json);
        Assert.Contains("\"type\":\"Episode\"", json);
        Assert.Contains("\"kind\":\"Episode\"", json);
    }

    [Fact]
    public void BadDeviceToken_retries_the_other_host()
    {
        Assert.Equal(new[] { false, true }, ApnsRouting.Hosts(false).ToArray());
        Assert.True(ApnsRouting.ShouldTryOtherHost(400, """{"reason":"BadDeviceToken"}"""));
        Assert.False(ApnsRouting.ShouldTryOtherHost(403, """{"reason":"Forbidden"}"""));
        Assert.False(ApnsRouting.ShouldTryOtherHost(500, "oops"));
        Assert.True(ApnsRouting.TokenIsDead(410, ""));
        Assert.False(ApnsRouting.TokenIsDead(400, """{"reason":"BadDeviceToken"}"""));
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}

public class DeviceStoreTests
{
    [Fact]
    public void Upsert_replaces_the_same_device()
    {
        var folder = Path.Combine(Path.GetTempPath(), "zelefin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new DeviceStore(folder);
            var device = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
            store.Upsert(device, ada, "token-1");
            store.Upsert(device, ada, "token-2");
            Assert.Equal(1, store.Count);
            Assert.Equal("token-2", store.All().Single().Token);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Remembers_the_session_for_one_device()
    {
        var folder = Path.Combine(Path.GetTempPath(), "zelefin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new DeviceStore(folder);
            var ios = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var android = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var user = Guid.Parse("11111111-1111-1111-1111-111111111111");
            store.Upsert(ios, user, "ios-token", "ios");
            store.Upsert(android, user, "android-token", "android");
            store.RememberSession("e6f1969f7bca9cf67fe9dde9a0b0333f", ios.ToString());
            store.RememberSession("bb2be81c-67d9-da2e-ef42-c5c9e21060b8", android.ToString());
            store.Upsert(ios, user, "ios-token-rotated", "ios");

            Assert.Equal([ios], store.ForSession("e6f1969f-7bca-9cf6-7fe9-dde9a0b0333f").Select(record => record.DeviceId));
            Assert.Equal([android], store.ForSession("bb2be81c67d9da2eef42c5c9e21060b8").Select(record => record.DeviceId));
            Assert.Equal([ios], store.ForDevice(ios).Select(record => record.DeviceId));
            Assert.Equal("ios-token-rotated", store.ForDevice(ios).Single().Token);
            Assert.Empty(store.ForSession("ffffffffffffffffffffffffffffffff"));
            store.RememberSessionUser("e6f1969f7bca9cf67fe9dde9a0b0333f", user);
            Assert.Equal(user, store.UserForSession("e6f1969f-7bca-9cf6-7fe9-dde9a0b0333f"));
            Assert.Equal(Guid.Empty, store.UserForSession("ffffffffffffffffffffffffffffffff"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Upsert_records_the_platform()
    {
        var folder = Path.Combine(Path.GetTempPath(), "zelefin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new DeviceStore(folder);
            var ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
            store.Upsert(Guid.NewGuid(), ada, "apns-token");
            store.Upsert(Guid.NewGuid(), ada, "fcm-token", "Android");
            store.Upsert(Guid.NewGuid(), ada, "other-token", "windows");
            var byToken = store.All().ToDictionary(record => record.Token);
            Assert.False(byToken["apns-token"].IsAndroid);
            Assert.True(byToken["fcm-token"].IsAndroid);
            Assert.Equal(DevicePlatform.Android, byToken["fcm-token"].Platform);
            Assert.Equal(DevicePlatform.Ios, byToken["other-token"].Platform);

            using var reloaded = new DeviceStore(folder);
            Assert.True(reloaded.All().Single(record => record.Token == "fcm-token").IsAndroid);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Rows_saved_before_android_support_load_as_ios()
    {
        var folder = Path.Combine(Path.GetTempPath(), "zelefin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "zelefin-devices.json"),
                """[{"token":"old","deviceId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","userId":"11111111-1111-1111-1111-111111111111","updatedAt":1}]""");
            using var store = new DeviceStore(folder);
            var record = store.All().Single();
            Assert.Equal(DevicePlatform.Ios, record.Platform);
            Assert.False(record.IsAndroid);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}

public class EventThrottleTests
{
    [Fact]
    public void Duplicate_keys_are_ignored_inside_the_window()
    {
        EventThrottle.Reset();
        Assert.False(EventThrottle.HasRecentlyProcessed("a", TimeSpan.FromMinutes(1)));
        Assert.True(EventThrottle.HasRecentlyProcessed("a", TimeSpan.FromMinutes(1)));
        Assert.False(EventThrottle.HasRecentlyProcessed("b", TimeSpan.FromMinutes(1)));
    }
}

public class DisplayMessageWatchTests
{
    [Theory]
    [InlineData("/Sessions/abc123/Message", "abc123", false)]
    [InlineData("/Sessions/abc123/Command", "abc123", true)]
    [InlineData("/jellyfin/Sessions/abc123/Message/", "abc123", false)]
    public void Matches_session_message_routes(string path, string sessionId, bool isCommand)
    {
        Assert.True(DisplayMessageWatch.TryMatchPath(path, out var id, out var command));
        Assert.Equal(sessionId, id);
        Assert.Equal(isCommand, command);
    }

    [Theory]
    [InlineData("/Sessions")]
    [InlineData("/Sessions/abc123/Playing")]
    [InlineData("/Items/abc123")]
    public void Ignores_other_routes(string path)
    {
        Assert.False(DisplayMessageWatch.TryMatchPath(path, out _, out _));
    }

    [Fact]
    public void Parses_dashboard_message_body()
    {
        Assert.True(DisplayMessageWatch.TryParse(
            """{"Header":"Zelefin","Text":"Server restart in 5 minutes","TimeoutMs":8000}""",
            false,
            out var header,
            out var text));
        Assert.Equal("Zelefin", header);
        Assert.Equal("Server restart in 5 minutes", text);
    }

    [Fact]
    public void Parses_general_command_display_message()
    {
        Assert.True(DisplayMessageWatch.TryParse(
            """{"Name":"DisplayMessage","Arguments":{"Header":"Admin","Text":"Hello"}}""",
            true,
            out var header,
            out var text));
        Assert.Equal("Admin", header);
        Assert.Equal("Hello", text);
    }

    [Fact]
    public void Ignores_other_general_commands()
    {
        Assert.False(DisplayMessageWatch.TryParse(
            """{"Name":"MoveUp","Arguments":{}}""",
            true,
            out _,
            out _));
    }

    [Fact]
    public void Push_type_is_session_message()
    {
        var message = DisplayMessageWatch.ToPush("Admin", "Hello");
        Assert.Equal("Admin", message.Title);
        Assert.Equal("Hello", message.Body);
        Assert.Equal("sessionMessage", message.Type);
    }

    [Fact]
    public void Session_ids_match_with_or_without_dashes()
    {
        Assert.True(DisplayMessageWatch.SameId("e6f1969f-7bca-9cf6-7fe9-dde9a0b0333f", "e6f1969f7bca9cf67fe9dde9a0b0333f"));
        Assert.False(DisplayMessageWatch.SameId("e6f1969f7bca9cf67fe9dde9a0b0333f", "bb2be81c67d9da2eef42c5c9e21060b8"));
    }
}

public class DeviceIdTests
{
    [Fact]
    public void Accepts_uuid_device_ids()
    {
        Assert.True(ZelefinController.TryDeviceId("B18E5910-21DD-49FE-A150-BB21EC3755E8", out var id));
        Assert.NotEqual(Guid.Empty, id);
        Assert.False(ZelefinController.TryDeviceId("not-a-uuid", out _));
        Assert.False(ZelefinController.TryDeviceId("", out _));
    }
}
