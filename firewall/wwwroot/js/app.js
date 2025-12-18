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
        this.piholeLogPolling = null; // Pour le polling des logs d'installation
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

    // ==========================================
    // PI-HOLE METHODS
    // ==========================================

    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            
            // Masquer toutes les sections d'abord
            document.getElementById('pihole-not-linux').style.display = 'none';
            document.getElementById('pihole-not-installed').style.display = 'none';
            document.getElementById('pihole-installed').style.display = 'none';

            // Vérifier si on est sur Linux
            if (!status.isLinux) {
                document.getElementById('pihole-not-linux').style.display = 'block';
                return;
            }

            // Vérifier si Pi-hole est installé
            if (!status.isInstalled) {
                document.getElementById('pihole-not-installed').style.display = 'block';
                return;
            }

            // Pi-hole est installé - afficher l'interface complète
            document.getElementById('pihole-installed').style.display = 'block';

            // Mettre à jour le statut
            const statusText = document.getElementById('pihole-status-text');
            const statusCard = document.getElementById('pihole-status-card');
            
            if (status.isRunning) {
                statusText.textContent = 'Actif';
                statusCard.classList.remove('danger');
                statusCard.classList.add('success');
            } else {
                statusText.textContent = 'Arrêté';
                statusCard.classList.remove('success');
                statusCard.classList.add('danger');
            }

            // Mettre à jour le statut de blocage
            const blockingText = document.getElementById('pihole-blocking-text');
            const blockingCard = document.getElementById('pihole-blocking-card');
            const btnEnable = document.getElementById('btn-enable-pihole');
            const btnDisable = document.getElementById('btn-disable-pihole');
            
            if (status.isEnabled) {
                blockingText.textContent = 'Activé';
                blockingCard.classList.remove('danger');
                blockingCard.style.color = 'var(--success)';
                btnEnable.style.display = 'none';
                btnDisable.style.display = 'inline-flex';
            } else {
                blockingText.textContent = 'Désactivé';
                blockingCard.style.color = 'var(--warning)';
                btnEnable.style.display = 'inline-flex';
                btnDisable.style.display = 'none';
            }

            // Mettre à jour la version
            document.getElementById('pihole-version').textContent = status.version || '-';

            // Charger les statistiques de l'API Pi-hole
            await this.loadPiholeSummary();

        } catch (error) {
            console.error('Erreur chargement Pi-hole:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger le statut Pi-hole', severity: 2 });
        }
    }

    async loadPiholeSummary() {
        try {
            const summary = await this.api('pihole/summary');
            
            if (summary && summary.status !== 'unavailable') {
                document.getElementById('ph-queries').textContent = this.formatNumber(summary.dnsQueriesToday || 0);
                document.getElementById('ph-blocked').textContent = this.formatNumber(summary.adsBlockedToday || 0);
                document.getElementById('ph-percent').textContent = (summary.adsPercentageToday || 0).toFixed(1) + '%';
                document.getElementById('ph-domains').textContent = this.formatNumber(summary.domainsBeingBlocked || 0);
                document.getElementById('ph-clients').textContent = summary.uniqueClients || 0;
                document.getElementById('ph-reply-ip').textContent = this.formatNumber(summary.replyIp || 0);
                document.getElementById('ph-reply-nx').textContent = this.formatNumber(summary.replyNxdomain || 0);
                
                // Gravity last updated
                if (summary.gravityLastUpdated && summary.gravityLastUpdated.relative) {
                    const rel = summary.gravityLastUpdated.relative;
                    if (rel.days > 0) {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.days}j ${rel.hours}h`;
                    } else if (rel.hours > 0) {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.hours}h ${rel.minutes}m`;
                    } else {
                        document.getElementById('ph-gravity').textContent = `Il y a ${rel.minutes}m`;
                    }
                }
            }
        } catch (error) {
            console.log('Pi-hole summary non disponible');
        }
    }

    formatNumber(num) {
        if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
        if (num >= 1000) return (num / 1000).toFixed(1) + 'k';
        return num.toString();
    }

    async installPihole() {
        if (!confirm('Voulez-vous installer Pi-hole ?\n\nCette opération peut prendre plusieurs minutes et nécessite une connexion Internet.')) {
            return;
        }

        try {
            // Afficher le modal de progression
            this.showPiholeInstallModal();
            
            const result = await this.api('pihole/install', { method: 'POST' });
            
            if (result.message) {
                this.showToast({ title: 'Installation', message: result.message, severity: 0 });
            }

            // Démarrer le polling des logs
            this.startPiholeLogPolling();

        } catch (error) {
            this.closePiholeInstallModal();
            this.showToast({ title: 'Erreur', message: 'Impossible de démarrer l\'installation: ' + error.message, severity: 3 });
        }
    }

    showPiholeInstallModal() {
        const modal = document.getElementById('pihole-install-modal');
        if (modal) {
            modal.classList.add('active');
            document.getElementById('pihole-install-logs').textContent = 'Démarrage de l\'installation...\n';
        }
    }

    closePiholeInstallModal() {
        const modal = document.getElementById('pihole-install-modal');
        if (modal) {
            modal.classList.remove('active');
        }
        this.stopPiholeLogPolling();
    }

    startPiholeLogPolling() {
        if (this.piholeLogPolling) return;
        
        let lastLength = 0;
        
        this.piholeLogPolling = setInterval(async () => {
            try {
                const result = await this.api('pihole/logs');
                const logsDiv = document.getElementById('pihole-install-logs');
                
                if (result.logs && logsDiv) {
                    // Ne montrer que les nouvelles lignes
                    if (result.logs.length > lastLength) {
                        logsDiv.textContent = result.logs;
                        logsDiv.scrollTop = logsDiv.scrollHeight;
                        lastLength = result.logs.length;
                    }
                    
                    // Vérifier si l'installation est terminée
                    if (result.logs.includes('=== Installation terminée')) {
                        this.stopPiholeLogPolling();
                        setTimeout(() => {
                            this.closePiholeInstallModal();
                            this.loadPihole();
                            this.showToast({ title: 'Succès', message: 'Pi-hole a été installé avec succès!', severity: 0 });
                        }, 2000);
                    }
                }
            } catch (error) {
                console.error('Erreur polling logs:', error);
            }
        }, 1000);
    }

    stopPiholeLogPolling() {
        if (this.piholeLogPolling) {
            clearInterval(this.piholeLogPolling);
            this.piholeLogPolling = null;
        }
    }

    async uninstallPihole() {
        if (!confirm('Êtes-vous sûr de vouloir désinstaller Pi-hole ?\n\nCette action supprimera Pi-hole et tous ses paramètres.')) {
            return;
        }

        try {
            this.showToast({ title: 'Désinstallation', message: 'Désinstallation de Pi-hole en cours...', severity: 1 });
            
            const result = await this.api('pihole/uninstall', { method: 'POST' });
            
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole a été désinstallé', severity: 0 });
            
            // Recharger la page Pi-hole
            setTimeout(() => this.loadPihole(), 1000);

        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de désinstaller Pi-hole: ' + error.message, severity: 3 });
        }
    }

    async enablePihole() {
        try {
            const result = await this.api('pihole/enable', { method: 'POST' });
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole activé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'activer Pi-hole: ' + error.message, severity: 3 });
        }
    }

    showDisablePiholeModal() {
        this.showModal('Désactiver Pi-hole', `
            <p>Pendant combien de temps voulez-vous désactiver le blocage ?</p>
            <div class="admin-buttons" style="flex-wrap: wrap; margin-top: 20px;">
                <button class="btn btn-warning" onclick="app.disablePihole(30)">30 secondes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(60)">1 minute</button>
                <button class="btn btn-warning" onclick="app.disablePihole(300)">5 minutes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(900)">15 minutes</button>
                <button class="btn btn-warning" onclick="app.disablePihole(3600)">1 heure</button>
                <button class="btn btn-danger" onclick="app.disablePihole()">Indéfiniment</button>
            </div>
        `);
    }

    async disablePihole(duration = null) {
        try {
            this.closeModal();
            
            const body = duration ? { duration } : {};
            const result = await this.api('pihole/disable', { 
                method: 'POST',
                body: JSON.stringify(body)
            });
            
            this.showToast({ title: 'Succès', message: result.message || 'Pi-hole désactivé', severity: 1 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de désactiver Pi-hole: ' + error.message, severity: 3 });
        }
    }

    showPiholePasswordModal() {
        this.showModal('Changer le mot de passe Pi-hole', `
            <div class="form-group">
                <label>Nouveau mot de passe</label>
                <input type="password" id="pihole-new-password" class="form-control" placeholder="Entrez le nouveau mot de passe">
            </div>
            <div class="form-group">
                <label>Confirmer le mot de passe</label>
                <input type="password" id="pihole-confirm-password" class="form-control" placeholder="Confirmez le mot de passe">
            </div>
            <p class="text-muted" style="font-size: 0.85rem; margin-top: 10px;">
                Laissez vide pour supprimer le mot de passe.
            </p>
        `, `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.setPiholePassword()">Enregistrer</button>
        `);
    }

    async setPiholePassword() {
        const password = document.getElementById('pihole-new-password').value;
        const confirm = document.getElementById('pihole-confirm-password').value;

        if (password !== confirm) {
            this.showToast({ title: 'Erreur', message: 'Les mots de passe ne correspondent pas', severity: 2 });
            return;
        }

        try {
            const result = await this.api('pihole/password', {
                method: 'POST',
                body: JSON.stringify({ password })
            });

            this.closeModal();
            this.showToast({ title: 'Succès', message: result.message || 'Mot de passe mis à jour', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de changer le mot de passe: ' + error.message, severity: 3 });
        }
    }

    // ==========================================
    // MODAL METHODS
    // ==========================================

    showModal(title, bodyHtml, footerHtml = '') {
        document.getElementById('modal-title').textContent = title;
        document.getElementById('modal-body').innerHTML = bodyHtml;
        document.getElementById('modal-footer').innerHTML = footerHtml;
        document.getElementById('modal').classList.add('active');
    }

    closeModal() {
        document.getElementById('modal').classList.remove('active');
    }

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
