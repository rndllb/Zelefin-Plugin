const pluginId = 'b18e5910-21dd-49fe-a150-bb21ec3755e8';

function tabs() {
    return [
        { href: Dashboard.getConfigurationPageUrl('Zelefin'), name: 'Application' },
        { href: Dashboard.getConfigurationPageUrl('ZelefinNotifications'), name: 'Notifications' }
    ];
}

export default function (view) {
    view.addEventListener('viewshow', () => {
        LibraryMenu.setTabs('Zelefin', 0, tabs);
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then((config) => {
            view.querySelector('#seerrUrl').value = config.SeerrUrl || '';
            view.querySelector('#seerrApiKey').value = '';
            view.querySelector('#autoSkipIntro').checked = !!config.AutoSkipIntro;
            view.querySelector('#autoSkipCredits').checked = !!config.AutoSkipCredits;
            view.querySelector('#lockAutoSkip').checked = !!config.LockAutoSkip;
            Dashboard.hideLoadingMsg();
        }).catch(() => Dashboard.hideLoadingMsg());
    });

    view.querySelector('#zelefin-config-form').addEventListener('submit', (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then((config) => {
            config.SeerrUrl = view.querySelector('#seerrUrl').value.trim();
            const seerrKey = view.querySelector('#seerrApiKey').value.trim();
            if (seerrKey) {
                config.SeerrApiKey = seerrKey;
            }
            config.AutoSkipIntro = view.querySelector('#autoSkipIntro').checked;
            config.AutoSkipCredits = view.querySelector('#autoSkipCredits').checked;
            config.LockAutoSkip = view.querySelector('#lockAutoSkip').checked;
            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(Dashboard.processPluginConfigurationUpdateResult)
            .catch(() => Dashboard.hideLoadingMsg());
    });
}
