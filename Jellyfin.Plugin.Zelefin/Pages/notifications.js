const pluginId = 'b18e5910-21dd-49fe-a150-bb21ec3755e8';

function tabs() {
    return [
        { href: Dashboard.getConfigurationPageUrl('Zelefin'), name: 'Application' },
        { href: Dashboard.getConfigurationPageUrl('ZelefinNotifications'), name: 'Notifications' }
    ];
}

export default function (view) {
    view.addEventListener('viewshow', async () => {
        LibraryMenu.setTabs('ZelefinNotifications', 1, tabs);
        view.querySelector('#notification-endpoint').textContent = ApiClient.getUrl('Zelefin/notification');
        Dashboard.showLoadingMsg();
        try {
            const config = await ApiClient.getPluginConfiguration(pluginId);
            view.querySelector('#itemAddedEnabled').checked = config.ItemAddedEnabled !== false;
            view.querySelector('#itemAddedGroupSeconds').value = config.ItemAddedGroupSeconds || 60;
            view.querySelector('#sessionStartedEnabled').checked = config.SessionStartedEnabled !== false;
            view.querySelector('#playbackStartedEnabled').checked = config.PlaybackStartedEnabled !== false;
            view.querySelector('#userLockedOutEnabled').checked = config.UserLockedOutEnabled !== false;
            view.querySelector('#eventThresholdSeconds').value = config.EventThresholdSeconds || 5;
            await renderLibraries(view, (config.ItemAddedLibraryIds || '').split(',').map((id) => id.trim()).filter(Boolean));
        } finally {
            Dashboard.hideLoadingMsg();
        }
    });

    view.querySelector('#send-test-notification').addEventListener('click', async () => {
        const status = view.querySelector('#test-notification-status');
        status.textContent = 'Sending…';
        try {
            const result = await ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('Zelefin/notification/test'),
                contentType: 'application/json',
                data: '{}',
                dataType: 'json'
            });
            const payload = typeof result === 'string' ? JSON.parse(result) : result;
            status.textContent = payload.ok
                ? 'Sent. Check your phone.'
                : (payload.error || 'Could not send a test alert.');
        } catch (error) {
            status.textContent = error?.message || 'Could not send a test alert.';
        }
    });

    view.querySelector('#zelefin-notification-form').addEventListener('submit', (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then((config) => {
            config.ItemAddedEnabled = view.querySelector('#itemAddedEnabled').checked;
            config.ItemAddedGroupSeconds = Number(view.querySelector('#itemAddedGroupSeconds').value) || 60;
            config.ItemAddedLibraryIds = selectedLibraryIds(view).join(',');
            config.SessionStartedEnabled = view.querySelector('#sessionStartedEnabled').checked;
            config.PlaybackStartedEnabled = view.querySelector('#playbackStartedEnabled').checked;
            config.UserLockedOutEnabled = view.querySelector('#userLockedOutEnabled').checked;
            config.EventThresholdSeconds = Number(view.querySelector('#eventThresholdSeconds').value) || 5;
            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(Dashboard.processPluginConfigurationUpdateResult)
            .catch(() => Dashboard.hideLoadingMsg());
    });
}

async function renderLibraries(view, selected) {
    const container = view.querySelector('#library-container');
    container.replaceChildren();
    const folders = await ApiClient.get('/Library/VirtualFolders').then((response) => response.json());
    if (!folders.length) {
        container.textContent = 'No libraries available';
        return;
    }
    folders.forEach((folder) => {
        const label = document.createElement('label');
        label.className = 'emby-checkbox-label';
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.setAttribute('is', 'emby-checkbox');
        input.dataset.libraryId = folder.ItemId;
        input.checked = selected.includes(folder.ItemId);
        const span = document.createElement('span');
        span.className = 'checkboxLabel';
        span.textContent = folder.Name;
        label.append(input, span);
        container.append(label);
    });
}

function selectedLibraryIds(view) {
    return Array.from(view.querySelectorAll('#library-container input[data-library-id]'))
        .filter((input) => input.checked)
        .map((input) => input.dataset.libraryId);
}
