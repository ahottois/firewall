// Network Firewall Monitor - Frontend Application

// Icon mapping - Font Awesome classes
const Icons = {
    shield: '<i class="fas fa-shield-alt"></i>',
    dashboard: '<i class="fas fa-chart-line"></i>',
    laptop: '<i class="fas fa-laptop"></i>',
    video: '<i class="fas fa-video"></i>',
    bell: '<i class="fas fa-bell"></i>',
    network: '<i class="fas fa-network-wired"></i>',
    settings: '<i class="fas fa-cog"></i>',
    search: '<i class="fas fa-search"></i>',
    check: '<i class="fas fa-check"></i>',
    times: '<i class="fas fa-times"></i>',
    eye: '<i class="fas fa-eye"></i>',
    sync: '<i class="fas fa-sync"></i>',
    download: '<i class="fas fa-download"></i>',
    upload: '<i class="fas fa-upload"></i>',
    plus: '<i class="fas fa-plus"></i>',
    lock: '<i class="fas fa-lock"></i>',
    unlock: '<i class="fas fa-unlock"></i>',
    warning: '<i class="fas fa-exclamation-triangle"></i>',
    danger: '<i class="fas fa-exclamation-circle"></i>',
    info: '<i class="fas fa-info-circle"></i>',
    online: '<i class="fas fa-circle" style="color: #00ff88;"></i>',
    offline: '<i class="fas fa-circle" style="color: #ff4757;"></i>',
    unknown: '<i class="fas fa-question-circle"></i>',
    newDevice: '<i class="fas fa-plus-circle"></i>',
    portScan: '<i class="fas fa-search"></i>',
    arpSpoof: '<i class="fas fa-user-secret"></i>',
    traffic: '<i class="fas fa-chart-bar"></i>',
    box: '<i class="fas fa-box"></i>',
    calendar: '<i class="fas fa-calendar"></i>',
    clock: '<i class="fas fa-clock"></i>',
    key: '<i class="fas fa-key"></i>',
    checkCircle: '<i class="fas fa-check-circle" style="color: #00ff88;"></i>',
    timesCircle: '<i class="fas fa-times-circle" style="color: #ff4757;"></i>',
    spinner: '<i class="fas fa-spinner fa-spin"></i>',
    trash: '<i class="fas fa-trash"></i>',
    redo: '<i class="fas fa-redo"></i>',
    terminal: '<i class="fas fa-terminal"></i>',
    ban: '<i class="fas fa-ban"></i>',
    unlockAlt: '<i class="fas fa-unlock-alt"></i>'
};

class FirewallApp {
    constructor() {
        this.currentPage = 'dashboard';
        this.eventSource = null;
        this.scanLogSource = null;
        this.currentCameraId = null;
        this.currentSnapshot = null;
        this.currentDevices = [];
        this.currentAgents = [];
        this.sortDirection = {};
        this.snifferInterval = null;
        this.deviceHub = null;
        this.init();
    }

    init() {
        this.setupNavigation();
        this.setupEventListeners();
        this.setupSorting();
        this.connectNotifications();
        this.connectDeviceHub();
        this.loadDashboard();
        this.startAutoRefresh();
    }

    // SignalR Device Hub Connection
    async connectDeviceHub() {
        if (typeof signalR === 'undefined') {
            console.warn('SignalR not loaded, real-time updates disabled');
            return;
        }

        try {
            this.deviceHub = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/devices')
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Nouveaux appareils découverts
            this.deviceHub.on('DeviceDiscovered', (device) => {
                console.log('Nouvel appareil découvert:', device);
                this.handleDeviceDiscovered(device);
            });

            // Appareil mis à jour
            this.deviceHub.on('DeviceUpdated', (device) => {
                console.log('Appareil mis à jour:', device);
                this.handleDeviceUpdated(device);
            });

            // Changement de statut (online/offline)
            this.deviceHub.on('DeviceStatusChanged', (device) => {
                console.log('Statut appareil changé:', device);
                this.handleDeviceStatusChanged(device);
            });

            // Appareil bloqué
            this.deviceHub.on('DeviceBlocked', (device) => {
                console.log('Appareil bloqué:', device);
                this.handleDeviceBlocked(device);
            });

            // Appareil débloqué
            this.deviceHub.on('DeviceUnblocked', (device) => {
                console.log('Appareil débloqué:', device);
                this.handleDeviceUnblocked(device);
            });

            // Progression du scan
            this.deviceHub.on('ScanProgress', (data) => {
                this.handleScanProgress(data);
            });

            // Scan terminé
            this.deviceHub.on('ScanComplete', (data) => {
                this.handleScanComplete(data);
            });

            this.deviceHub.onclose(() => {
                console.log('DeviceHub déconnecté');
            });

            this.deviceHub.onreconnecting(() => {
                console.log('DeviceHub reconnexion en cours...');
            });

            this.deviceHub.onreconnected(() => {
                console.log('DeviceHub reconnecté');
            });

            await this.deviceHub.start();
            console.log('Connecté au DeviceHub');
        } catch (error) {
            console.error('Erreur connexion DeviceHub:', error);
            setTimeout(() => this.connectDeviceHub(), 5000);
        }
    }

    // Handlers SignalR
    handleDeviceDiscovered(device) {
        // Ajouter à la liste si on est sur la page devices
        const existingIndex = this.currentDevices.findIndex(d => d.macAddress === device.macAddress);
        if (existingIndex === -1) {
            this.currentDevices.push(device);
        } else {
            this.currentDevices[existingIndex] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Nouvel appareil',
            message: `${device.macAddress} (${device.ipAddress || 'IP inconnue'})`,
            severity: 0
        });
    }

    handleDeviceUpdated(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id || d.macAddress === device.macAddress);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
    }

    handleDeviceStatusChanged(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id || d.macAddress === device.macAddress);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
    }

    handleDeviceBlocked(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Appareil bloqué',
            message: `${device.macAddress} a été bloqué`,
            severity: 2
        });
    }

    handleDeviceUnblocked(device) {
        const index = this.currentDevices.findIndex(d => d.id === device.id);
        if (index !== -1) {
            this.currentDevices[index] = device;
        }
        
        if (this.currentPage === 'devices') {
            this.addOrUpdateDeviceRow(device);
        }
        
        this.showToast({
            title: 'Appareil débloqué',
            message: `${device.macAddress} a été débloqué`,
            severity: 0
        });
    }

    handleScanProgress(data) {
        const scanStatus = document.getElementById('scan-status');
        if (scanStatus) {
            scanStatus.textContent = `Scan: ${data.scanned}/${data.total} (${data.found} trouvés)`;
        }
    }

    handleScanComplete(data) {
        const scanStatus = document.getElementById('scan-status');
        if (scanStatus) {
            scanStatus.textContent = `Scan terminé: ${data.totalDevices} appareils actifs`;
            setTimeout(() => { scanStatus.textContent = ''; }, 5000);
        }
    }

    // Ajouter ou mettre à jour une ligne dans le tableau
    addOrUpdateDeviceRow(device) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) return;

        const existingRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
        const rowHtml = this.createDeviceRowHtml(device);

        if (existingRow) {
            existingRow.outerHTML = rowHtml;
            // Animation de mise à jour
            const newRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
            if (newRow) {
                newRow.classList.add('row-updated');
                setTimeout(() => newRow.classList.remove('row-updated'), 1000);
            }
        } else {
            tbody.insertAdjacentHTML('afterbegin', rowHtml);
            // Animation d'ajout
            const newRow = tbody.querySelector(`tr[data-mac="${device.macAddress}"]`);
            if (newRow) {
                newRow.classList.add('row-new');
                setTimeout(() => newRow.classList.remove('row-new'), 2000);
            }
        }
    }

    createDeviceRowHtml(device) {
        const isBlocked = device.status === 3 || device.status === 'Blocked';
        return `
            <tr data-mac="${this.escapeHtml(device.macAddress)}" data-id="${device.id}">
                <td><span class="status-badge ${this.getStatusClass(device.status)}">${this.getStatusText(device.status)}</span></td>
                <td class="device-mac">${this.escapeHtml(device.macAddress)}</td>
                <td>${this.escapeHtml(device.ipAddress || '-')}</td>
                <td>${this.escapeHtml(device.vendor || 'Inconnu')}</td>
                <td>${this.escapeHtml(device.description || device.hostname || '-')}</td>
                <td>${this.formatDate(device.lastSeen)}</td>
                <td>
                    <div class="action-buttons" style="display: flex; gap: 5px;">
                        ${!device.isTrusted ? `<button class="btn btn-sm btn-success" onclick="app.approveDevice(${device.id})" title="Approuver">${Icons.check}</button>` : ''}
                        <button class="btn btn-sm btn-primary" onclick="app.viewDevice(${device.id})" title="Détails">${Icons.eye}</button>
                        ${isBlocked 
                            ? `<button class="btn btn-sm btn-warning" onclick="app.unblockDevice(${device.id})" title="Débloquer">${Icons.unlockAlt}</button>`
                            : `<button class="btn btn-sm btn-danger" onclick="app.blockDevice(${device.id})" title="Bloquer">${Icons.ban}</button>`
                        }
                        <button class="btn btn-sm btn-secondary" onclick="app.deleteDevice(${device.id})" title="Supprimer">${Icons.trash}</button>
                    </div>
                </td>
            </tr>
        `;
    }

    // Bloquer un appareil
    async blockDevice(id) {
        if (!confirm('Êtes-vous sûr de vouloir bloquer cet appareil ?')) return;
        
        try {
            await this.api(`devices/${id}/block`, { method: 'POST' });
            // Le hub SignalR notifiera la mise à jour
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de bloquer l\'appareil: ' + error.message, severity: 3 });
        }
    }

    // Débloquer un appareil
    async unblockDevice(id) {
        try {
            await this.api(`devices/${id}/unblock`, { method: 'POST' });
            // Le hub SignalR notifiera la mise à jour
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de débloquer l\'appareil: ' + error.message, severity: 3 });
        }
    }

    // Navigation
    setupNavigation() {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', () => {
                const page = item.dataset.page;
                this.navigateTo(page);
            });
        });

        document.querySelectorAll('.view-all').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                const page = link.dataset.page;
                this.navigateTo(page);
            });
        });
    }

    navigateTo(page = 'dashboard') {
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.page === page);
        });

        document.querySelectorAll('.page').forEach(p => {
            p.classList.toggle('active', p.id === `${page}-page`);
        });

        const titles = {
            dashboard: 'Dashboard',
            devices: 'Appareils Reseau',
            cameras: 'Cameras Reseau',
            alerts: 'Alertes',
            traffic: 'Trafic Reseau',
            pihole: 'Pi-hole Manager',
            dhcp: 'Serveur DHCP',
            setup: 'Installation & Configuration',
            sniffer: 'Packet Sniffer',
            router: 'Routeur / NAT',
            settings: 'Parametres',
            admin: 'Administration'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;
        this.loadPageData(page);
        
        // Stop sniffer polling if leaving sniffer page
        if (page !== 'sniffer' && this.snifferInterval) {
            clearInterval(this.snifferInterval);
            this.snifferInterval = null;
        }
    }

    loadPageData(page) {
        switch (page) {
            case 'dashboard': this.loadDashboard(); break;
            case 'devices': this.loadDevices(); break;
            case 'agents': this.loadAgents(); break;
            case 'cameras': this.loadCameras(); break;
            case 'alerts': this.loadAlerts(); break;
            case 'traffic': this.loadTraffic(); break;
            case 'pihole': this.loadPihole(); break;
            case 'dhcp': this.loadDhcp(); break;
            case 'setup': /* Static content */ break;
            case 'sniffer': this.loadSniffer(); break;
            case 'router': this.loadRouter(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }

    setupEventListeners() {
        const scanBtn = document.getElementById('scan-btn');
        if (scanBtn) scanBtn.addEventListener('click', () => this.scanNetwork());
        
        const markAllReadBtn = document.getElementById('mark-all-read');
        if (markAllReadBtn) markAllReadBtn.addEventListener('click', () => this.markAllAlertsRead());
        
        document.getElementById('scan-cameras-btn')?.addEventListener('click', () => this.scanCameras());
        document.getElementById('add-camera-btn')?.addEventListener('click', () => this.showAddCameraModal());
        
        document.querySelectorAll('.filter-buttons .btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.filter-buttons .btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.loadDevices(e.target.dataset.filter);
            });
        });

        const modalClose = document.querySelector('.modal-close');
        if (modalClose) modalClose.addEventListener('click', () => this.closeModal());
        
        const modal = document.getElementById('modal');
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target.id === 'modal') this.closeModal();
            });
        }
        
        const cameraModal = document.getElementById('camera-modal');
        if (cameraModal) {
            cameraModal.addEventListener('click', (e) => {
                if (e.target.id === 'camera-modal') this.closeCameraModal();
            });
        }
    }

    setupSorting() {
        document.querySelectorAll('.sortable').forEach(header => {
            header.addEventListener('click', () => {
                const column = header.dataset.sort;
                this.sortDevices(column);
            });
        });
    }

    sortDevices(column = 'id') {
        // Toggle sort direction
        this.sortDirection[column] = this.sortDirection[column] === 'asc' ? 'desc' : 'asc';
        const direction = this.sortDirection[column];

        // Update icons
        document.querySelectorAll('.sortable i').forEach(icon => icon.className = 'fas fa-sort');
        const activeHeader = document.querySelector(`.sortable[data-sort="${column}"] i`);
        if (activeHeader) {
            activeHeader.className = direction === 'asc' ? 'fas fa-sort-up' : 'fas fa-sort-down';
        }

        // Sort data
        this.currentDevices.sort((a, b) => {
            let valA = a[column];
            let valB = b[column];

            // Handle specific columns
            if (column === 'ip') {
                // Simple IP sort (can be improved for numeric sort)
                valA = valA || '';
                valB = valB || '';
            } else if (column === 'lastSeen') {
                valA = new Date(valA).getTime();
                valB = new Date(valB).getTime();
            } else if (column === 'status') {
                // Sort by status enum value
                valA = valA || 0;
                valB = valB || 0;
            } else {
                // String sort
                valA = (valA || '').toString().toLowerCase();
                valB = (valB || '').toString().toLowerCase();
            }

            if (valA < valB) return direction === 'asc' ? -1 : 1;
            if (valA > valB) return direction === 'asc' ? 1 : -1;
            return 0;
        });

        this.renderDevicesTable(this.currentDevices);
    }

    // API Calls
    async api(endpoint, options = {}) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                ...options,
                headers: { 'Content-Type': 'application/json', ...options.headers }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            // Check if response has content
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                const text = await response.text();
                return text ? JSON.parse(text) : {};
            }
            return {};
        } catch (error) {
            if (!options.suppressErrorLog) {
                console.error(`API Error (${endpoint}):`, error);
            }
            throw error;
        }
    }

    // API call that doesn't expect JSON response
    async apiPost(endpoint) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return { success: true };
        } catch (error) {
            console.error(`API Error (${endpoint}):`, error);
            throw error;
        }
    }

    // Real-time Notifications
    connectNotifications() {
        if (this.eventSource) this.eventSource.close();

        this.eventSource = new EventSource('/api/notifications/stream');

        this.eventSource.addEventListener('alert', (e) => {
            const alert = JSON.parse(e.data);
            this.showToast(alert);
            this.updateAlertBadge();
            if (this.currentPage === 'dashboard') this.loadDashboard();
            else if (this.currentPage === 'alerts') this.loadAlerts();
        });

        this.eventSource.addEventListener('connected', () => console.log('Connected to notification stream'));

        this.eventSource.onerror = () => {
            console.log('Notification stream disconnected, reconnecting...');
            setTimeout(() => this.connectNotifications(), 5000);
        };
    }

    // Devices - mise à jour de loadDevices pour gérer le filtrage par statut
    async loadDevices(filter = 'all') {
        try {
            let endpoint = 'devices';
            if (filter === 'online') endpoint = 'devices/online';
            else if (filter === 'unknown') endpoint = 'devices/unknown';
            else if (filter === 'blocked') endpoint = 'devices/blocked';

            const devices = await this.api(endpoint);
            this.currentDevices = devices;
            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    }

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!tbody) return;

        if (!devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil trouvé</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => this.createDeviceRowHtml(device)).join('');
    }

    // API Calls - grouping related functions together
    async api(endpoint, options = {}) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                ...options,
                headers: { 'Content-Type': 'application/json', ...options.headers }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            
            // Check if response has content
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('application/json')) {
                const text = await response.text();
                return text ? JSON.parse(text) : {};
            }
            return {};
        } catch (error) {
            if (!options.suppressErrorLog) {
                console.error(`API Error (${endpoint}):`, error);
            }
            throw error;
        }
    }

    async apiPost(endpoint) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return { success: true };
        } catch (error) {
            console.error(`API Error (${endpoint}):`, error);
            throw error;
        }
    }

    // Real-time Notifications - merged connectNotifications and Device Hub handlers
    connectNotifications() {
        if (this.eventSource) this.eventSource.close();

        this.eventSource = new EventSource('/api/notifications/stream');

        this.eventSource.addEventListener('alert', (e) => {
            const alert = JSON.parse(e.data);
            this.showToast(alert);
            this.updateAlertBadge();
            if (this.currentPage === 'dashboard') this.loadDashboard();
            else if (this.currentPage === 'alerts') this.loadAlerts();
        });

        this.eventSource.addEventListener('connected', () => console.log('Connected to notification stream'));

        this.eventSource.onerror = () => {
            console.log('Notification stream disconnected, reconnecting...');
            setTimeout(() => this.connectNotifications(), 5000);
        };

        // Connect Device Hub for real-time device updates
        this.connectDeviceHub();
    }

    // ...rest of existing code remains the same...

    // Clear all intervals and timeouts on unload
    unload() {
        if (this.eventSource) this.eventSource.close();
        if (this.scanLogSource) clearInterval(this.scanLogSource);
        if (this.snifferInterval) clearInterval(this.snifferInterval);
        if (this.deviceHub) {
            this.deviceHub.stop().catch(err => console.error('Error stopping device hub:', err));
        }
    }
}

// Initialize app
const app = new FirewallApp();

window.addEventListener('beforeunload', () => {
    app.unload();
});
