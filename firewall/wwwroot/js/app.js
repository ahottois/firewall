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
    terminal: '<i class="fas fa-terminal"></i>'
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
        this.init();
    }

    init() {
        this.setupNavigation();
        this.setupEventListeners();
        this.setupSorting();
        this.connectNotifications();
        this.loadDashboard();
        this.startAutoRefresh();
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

    // Devices
    async loadDevices(filter = 'all') {
        try {
            let endpoint = 'devices';
            if (filter === 'online') endpoint = 'devices/online';
            else if (filter === 'unknown') endpoint = 'devices/unknown';

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
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil trouve</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => `
            <tr>
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
                        <button class="btn btn-sm btn-danger" onclick="app.deleteDevice(${device.id})" title="Supprimer">${Icons.trash}</button>
                    </div>
                </td>
            </tr>
        `).join('');
    }

    async approveDevice(id) {
        try {
            await this.api(`devices/${id}/trust`, { 
                method: 'POST',
                body: JSON.stringify({ trusted: true })
            });
            this.showToast({ title: 'Succes', message: 'Appareil approuve', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async viewDevice(id) {
        try {
            const device = await this.api(`devices/${id}`);
            
            document.getElementById('device-id').value = device.id;
            document.getElementById('device-mac').value = device.macAddress;
            document.getElementById('device-ip').value = device.ipAddress || '';
            document.getElementById('device-vendor').value = device.vendor || '';
            document.getElementById('device-hostname').value = device.hostname || '';
            document.getElementById('device-description').value = device.description || '';
            document.getElementById('device-is-known').checked = device.isKnown;
            document.getElementById('device-is-trusted').checked = device.isTrusted;
            
            document.getElementById('device-first-seen').textContent = new Date(device.firstSeen).toLocaleString();
            document.getElementById('device-last-seen').textContent = new Date(device.lastSeen).toLocaleString();
            
            document.getElementById('device-details-modal').classList.add('active');
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async saveDeviceDetails() {
        const id = document.getElementById('device-id').value;
        const data = {
            ipAddress: document.getElementById('device-ip').value,
            vendor: document.getElementById('device-vendor').value,
            hostname: document.getElementById('device-hostname').value,
            description: document.getElementById('device-description').value,
            isKnown: document.getElementById('device-is-known').checked,
            isTrusted: document.getElementById('device-is-trusted').checked
        };

        try {
            await this.api(`devices/${id}`, {
                method: 'PUT',
                body: JSON.stringify(data)
            });
            
            document.getElementById('device-details-modal').classList.remove('active');
            this.showToast({ title: 'Succes', message: 'Appareil mis a jour', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async deleteDevice(id) {
        if (!confirm('Etes-vous sur de vouloir supprimer cet appareil ?')) return;
        try {
            await this.api(`devices/${id}`, { method: 'DELETE' });
            this.showToast({ title: 'Succes', message: 'Appareil supprime', severity: 0 });
            this.loadDevices();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    // Agents
    async loadAgents() {
        try {
            const agents = await this.api('agents');
            this.currentAgents = agents; // Store for viewAgent
            this.renderAgents(agents);
        } catch (error) {
            console.error('Error loading agents:', error);
        }
    }

    renderAgents(agents) {
        const grid = document.getElementById('agents-grid');
        const empty = document.getElementById('agents-empty');
        
        if (!grid || !empty) return;

        if (!agents || agents.length === 0) {
            grid.innerHTML = '';
            empty.style.display = 'block';
            return;
        }

        empty.style.display = 'none';
        grid.innerHTML = agents.map(agent => `
            <div class="card agent-card">
                <div class="card-header">
                    <h3>${this.escapeHtml(agent.hostname)}</h3>
                    <span class="status-badge ${agent.status === 'Online' ? 'online' : 'offline'}">${agent.status}</span>
                </div>
                <div class="card-body">
                    <p><strong>OS:</strong> ${this.escapeHtml(agent.os)}</p>
                    <p><strong>IP:</strong> ${this.escapeHtml(agent.ipAddress)}</p>
                    <p><strong>Last Seen:</strong> ${this.formatDate(agent.lastSeen)}</p>
                    <p><strong>Version:</strong> ${this.escapeHtml(agent.version)}</p>
                    <div style="margin-top: 15px; display: flex; gap: 10px;">
                        <button class="btn btn-sm btn-primary" onclick="app.viewAgent(${agent.id})" style="width: 100%;">
                            <i class="fas fa-info-circle"></i> D&eacute;tails
                        </button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteAgent(${agent.id})">
                            <i class="fas fa-trash"></i>
                        </button>
                    </div>
                </div>
            </div>
        `).join('');
    }

    async viewAgent(id) {
        const agent = this.currentAgents ? this.currentAgents.find(a => a.id === id) : null;
        if (!agent) return;

        document.getElementById('agent-detail-hostname').textContent = agent.hostname;
        document.getElementById('agent-detail-os').textContent = agent.os;
        document.getElementById('agent-detail-ip').textContent = agent.ipAddress;
        document.getElementById('agent-detail-seen').textContent = this.formatDate(agent.lastSeen);

        // Parse Details
        let details = {};
        try {
            if (agent.detailsJson) {
                details = JSON.parse(agent.detailsJson);
            }
        } catch (e) {
            console.error('Error parsing agent details', e);
        }

        // Render Hardware
        const hw = details.hardware || {};
        document.getElementById('agent-hardware-info').innerHTML = `
            <p><strong>CPU:</strong> ${this.escapeHtml(hw.cpuModel || 'N/A')}</p>
            <p><strong>Cores:</strong> ${hw.cpuCores || 'N/A'}</p>
            <p><strong>RAM:</strong> ${hw.totalMemoryMb ? Math.round(hw.totalMemoryMb / 1024 * 10) / 10 + ' GB' : 'N/A'}</p>
            <p><strong>Disques:</strong> ${this.escapeHtml(hw.disks || 'N/A')}</p>
        `;

        // Render Network
        const net = details.network || {};
        document.getElementById('agent-network-info').innerHTML = `
            <p><strong>Interfaces:</strong> ${this.escapeHtml(net.interfaces || 'N/A')}</p>
            <p><strong>MACs:</strong> ${this.escapeHtml(net.macAddresses || 'N/A')}</p>
        `;

        // Render System
        const sys = details.system || {};
        document.getElementById('agent-system-info').innerHTML = `
            <p><strong>Kernel:</strong> ${this.escapeHtml(sys.kernel || 'N/A')}</p>
            <p><strong>Uptime:</strong> ${this.escapeHtml(sys.uptime || 'N/A')}</p>
            ${sys.manufacturer ? `<p><strong>Fabricant:</strong> ${this.escapeHtml(sys.manufacturer)}</p>` : ''}
            ${sys.model ? `<p><strong>Mod&egrave;le:</strong> ${this.escapeHtml(sys.model)}</p>` : ''}
        `;

        document.getElementById('agent-details-modal').classList.add('active');
    }

    async deleteAgent(id) {
        if (!confirm('Supprimer cet agent ?')) return;
        try {
            await this.api(`agents/${id}`, { method: 'DELETE' });
            this.loadAgents();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    // Cameras
    async loadCameras() {
        try {
            const cameras = await this.api('cameras');
            this.renderCameras(cameras);
            
            // Update stats
            document.getElementById('cameras-total').textContent = cameras.length;
            document.getElementById('cameras-online').textContent = cameras.filter(c => c.status === 'Online').length;
            document.getElementById('cameras-vulnerable').textContent = cameras.filter(c => c.isVulnerable).length;
            document.getElementById('cameras-secured').textContent = cameras.filter(c => !c.isVulnerable && c.status === 'Online').length;
        } catch (error) {
            console.error('Error loading cameras:', error);
        }
    }

    renderCameras(cameras) {
        const grid = document.getElementById('cameras-grid');
        if (!grid) return;

        if (!cameras || cameras.length === 0) {
            grid.innerHTML = '<div class="empty-state">Aucune camera detectee</div>';
            return;
        }

        grid.innerHTML = cameras.map(camera => `
            <div class="camera-card ${camera.isVulnerable ? 'vulnerable' : 'secured'}">
                <div class="camera-preview" onclick="app.viewCamera(${camera.id})">
                    <div class="camera-preview-placeholder">
                        ${Icons.video}
                        <span>${this.escapeHtml(camera.vendor || 'Camera')}</span>
                    </div>
                    <div class="camera-status-overlay">
                        <span class="camera-status-badge ${camera.status.toLowerCase()}">${camera.status}</span>
                        ${camera.isVulnerable ? '<span class="camera-status-badge vulnerable">VULNERABLE</span>' : ''}
                    </div>
                </div>
                <div class="camera-info">
                    <div class="camera-name">${this.escapeHtml(camera.name || camera.hostname || 'Unknown Camera')}</div>
                    <div class="camera-address">${this.escapeHtml(camera.ipAddress)}</div>
                    <div class="camera-meta">
                        <span><i class="fas fa-network-wired"></i> Port ${camera.port}</span>
                        <span><i class="fas fa-clock"></i> ${this.formatDate(camera.lastSeen)}</span>
                    </div>
                </div>
            </div>
        `).join('');
    }

    async scanCameras() {
        try {
            document.getElementById('scan-cameras-btn').disabled = true;
            document.getElementById('scan-logs-panel').style.display = 'block';
            
            await this.api('cameras/scan', { method: 'POST' });
            
            // Start polling for logs
            if (this.scanLogSource) clearInterval(this.scanLogSource);
            this.scanLogSource = setInterval(() => this.loadScanLogs(), 1000);
            
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
            document.getElementById('scan-cameras-btn').disabled = false;
        }
    }

    // Alerts
    async loadAlerts() {
        try {
            const alerts = await this.api('alerts?count=50');
            this.renderAlerts(alerts);
        } catch (error) {
            console.error('Error loading alerts:', error);
        }
    }

    renderAlerts(alerts) {
        const list = document.getElementById('alerts-list');
        if (!list) return;

        if (!alerts || alerts.length === 0) {
            list.innerHTML = '<div class="empty-state">Aucune alerte</div>';
            return;
        }

        list.innerHTML = alerts.map(alert => `
            <div class="alert-item ${this.getSeverityClass(alert.severity)} ${!alert.isRead ? 'unread' : ''}">
                <div class="alert-content">
                    <div class="alert-title">${this.getAlertTypeIcon(alert.type)} ${this.escapeHtml(alert.title)}</div>
                    <div class="alert-message">${this.escapeHtml(alert.message)}</div>
                    <div class="alert-time">${this.formatDate(alert.timestamp)}</div>
                </div>
                <div class="alert-actions">
                    ${!alert.isRead ? `<button class="btn btn-sm" onclick="app.markAlertRead(${alert.id})">${Icons.check}</button>` : ''}
                </div>
            </div>
        `).join('');
    }

    async markAllAlertsRead() {
        try {
            await this.api('alerts/read-all', { method: 'POST' });
            this.loadAlerts();
            this.updateAlertBadge();
        } catch (error) {
            console.error('Error marking alerts read:', error);
        }
    }

    async resolveAllAlerts() {
        if (!confirm('Tout marquer comme resolu ?')) return;
        try {
            await this.api('alerts/resolve-all', { method: 'POST' });
            this.loadAlerts();
        } catch (error) {
            console.error('Error resolving alerts:', error);
        }
    }

    async resetAllAlerts() {
        if (!confirm('Supprimer tout l\'historique des alertes ?')) return;
        try {
            await this.api('alerts/reset', { method: 'POST' });
            this.loadAlerts();
        } catch (error) {
            console.error('Error resetting alerts:', error);
        }
    }

    updateAlertBadge() {
        this.api('alerts/unread-count').then(data => {
            const badge = document.getElementById('notification-count');
            if (badge) {
                badge.textContent = data.count;
                badge.style.display = data.count > 0 ? 'block' : 'none';
            }
        }).catch(e => console.error(e));
    }

    // Traffic
    async loadTraffic() {
        try {
            const stats = await this.api('traffic/stats?hours=24');
            
            document.getElementById('total-packets').textContent = this.formatNumber(stats.totalPackets);
            document.getElementById('inbound-packets').textContent = this.formatNumber(stats.inboundPackets);
            document.getElementById('outbound-packets').textContent = this.formatNumber(stats.outboundPackets);
            document.getElementById('suspicious-packets').textContent = this.formatNumber(stats.suspiciousPackets);

            // Render protocols chart
            const protocolsChart = document.getElementById('protocols-chart');
            if (protocolsChart && stats.topProtocols) {
                const max = Math.max(...Object.values(stats.topProtocols));
                protocolsChart.innerHTML = Object.entries(stats.topProtocols).map(([proto, count]) => `
                    <div class="protocol-item">
                        <span style="width: 60px;">${proto}</span>
                        <div class="protocol-bar">
                            <div class="protocol-bar-fill" style="width: ${(count / max * 100)}%"></div>
                        </div>
                        <span>${this.formatNumber(count)}</span>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading traffic:', error);
        }
    }

    // Sniffer
    async loadSniffer() {
        if (!this.snifferInterval) {
            this.snifferInterval = setInterval(() => this.refreshSnifferPackets(), 1000);
        }
        this.refreshSnifferPackets();
    }

    async refreshSnifferPackets() {
        try {
            const packets = await this.api('sniffer/packets');
            this.renderSnifferPackets(packets);
        } catch (error) {
            console.error('Error loading packets:', error);
        }
    }

    renderSnifferPackets(packets) {
        const tbody = document.getElementById('sniffer-packets-table');
        if (!tbody) return;

        tbody.innerHTML = packets.map(p => `
            <tr>
                <td>${new Date(p.timestamp).toLocaleTimeString()}</td>
                <td>${this.escapeHtml(p.sourceIp || p.sourceMac)}:${p.sourcePort || ''}</td>
                <td>${this.escapeHtml(p.destinationIp || p.destinationMac)}:${p.destinationPort || ''}</td>
                <td>${this.escapeHtml(p.protocol)}</td>
                <td>${p.packetSize}</td>
                <td>${p.direction}</td>
            </tr>
        `).join('');
    }

    async startSniffer() {
        const filter = {
            sourceIp: document.getElementById('sniffer-filter-ip').value,
            port: parseInt(document.getElementById('sniffer-filter-port').value) || null,
            protocol: document.getElementById('sniffer-filter-proto').value,
            direction: document.getElementById('sniffer-filter-direction').value
        };

        try {
            await this.api('sniffer/start', { method: 'POST', body: JSON.stringify(filter) });
            document.getElementById('btn-start-sniffer').style.display = 'none';
            document.getElementById('btn-stop-sniffer').style.display = 'inline-block';
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async stopSniffer() {
        try {
            await this.api('sniffer/stop', { method: 'POST' });
            document.getElementById('btn-start-sniffer').style.display = 'inline-block';
            document.getElementById('btn-stop-sniffer').style.display = 'none';
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async clearSnifferPackets() {
        try {
            await this.api('sniffer/clear', { method: 'POST' });
            this.refreshSnifferPackets();
        } catch (error) {
            console.error(error);
        }
    }

    // Router
    async loadRouter() {
        this.loadRouterInterfaces();
        this.loadRouterMappings();
    }

    async loadRouterInterfaces() {
        try {
            const interfaces = await this.api('router/interfaces');
            const container = document.getElementById('router-interfaces-list');
            if (!container) return;

            container.innerHTML = interfaces.map(iface => `
                <div class="interface-item">
                    <div>
                        <strong>${this.escapeHtml(iface.name)}</strong>
                        <div class="device-ip">${this.escapeHtml(iface.description)}</div>
                        ${iface.macAddress ? `<div class="device-ip">MAC: ${this.escapeHtml(iface.macAddress)}</div>` : ''}
                        ${iface.addresses ? iface.addresses.map(addr => `<div class="device-ip">${addr}</div>`).join('') : ''}
                    </div>
                    <span class="status-badge ${iface.isUp ? 'online' : 'offline'}">${iface.isUp ? 'Up' : 'Down'}</span>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading router interfaces:', error);
        }
    }

    async loadRouterMappings() {
        try {
            const mappings = await this.api('router/mappings');
            const tbody = document.getElementById('router-mappings-table');
            if (!tbody) return;

            if (!mappings || mappings.length === 0) {
                tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucune regle de port mapping</td></tr>';
                return;
            }

            tbody.innerHTML = mappings.map(m => `
                <tr>
                    <td>${this.escapeHtml(m.name)}</td>
                    <td>${m.listenPort}</td>
                    <td>${this.escapeHtml(m.targetIp)}:${m.targetPort}</td>
                    <td>${m.protocol}</td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteMapping(${m.id})">${Icons.trash}</button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading mappings:', error);
        }
    }

    async deleteMapping(id) {
        if (!confirm('Supprimer cette regle ?')) return;
        try {
            await this.api(`router/mappings/${id}`, { method: 'DELETE' });
            this.loadRouterMappings();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    showAddMappingModal() {
        document.getElementById('modal-title').textContent = 'Ajouter une regle de port mapping';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom</label>
                <input type="text" id="mapping-name" class="form-control" placeholder="Ex: Serveur Web">
            </div>
            <div class="form-group">
                <label>Port Local (Entrant)</label>
                <input type="number" id="mapping-port" class="form-control" placeholder="80">
            </div>
            <div class="form-group">
                <label>IP Cible</label>
                <input type="text" id="mapping-target-ip" class="form-control" placeholder="192.168.1.x">
            </div>
            <div class="form-group">
                <label>Port Cible</label>
                <input type="number" id="mapping-target-port" class="form-control" placeholder="80">
            </div>
            <div class="form-group">
                <label>Protocole</label>
                <select id="mapping-protocol" class="form-control">
                    <option value="TCP">TCP</option>
                    <option value="UDP">UDP</option>
                </select>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.saveMapping()">Ajouter</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async saveMapping() {
        const data = {
            name: document.getElementById('mapping-name').value,
            listenPort: parseInt(document.getElementById('mapping-port').value),
            targetIp: document.getElementById('mapping-target-ip').value,
            targetPort: parseInt(document.getElementById('mapping-target-port').value),
            protocol: document.getElementById('mapping-protocol').value
        };

        try {
            await this.api('router/mappings', { method: 'POST', body: JSON.stringify(data) });
            this.closeModal();
            this.loadRouterMappings();
            this.showToast({ title: 'Succes', message: 'Regle ajoutee', severity: 0 });
        } catch (error) {
            alert('Erreur: ' + error.message);
        }
    }

    showAddDeviceModal() {
        document.getElementById('device-id').value = '';
        document.getElementById('device-mac').value = '';
        document.getElementById('device-mac').readOnly = false;
        document.getElementById('device-mac').style.background = '';
        document.getElementById('device-ip').value = '';
        document.getElementById('device-vendor').value = '';
        document.getElementById('device-hostname').value = '';
        document.getElementById('device-description').value = '';
        document.getElementById('device-is-known').checked = true;
        document.getElementById('device-is-trusted').checked = false;
        
        document.getElementById('device-first-seen').textContent = '-';
        document.getElementById('device-last-seen').textContent = '-';
        
        // Override save button to create instead of update
        const footer = document.querySelector('#device-details-modal .modal-footer');
        const originalBtn = footer.querySelector('.btn-primary');
        originalBtn.onclick = () => this.createDevice();
        
        document.getElementById('device-details-modal').classList.add('active');
    }

    async createDevice() {
        const data = {
            macAddress: document.getElementById('device-mac').value,
            ipAddress: document.getElementById('device-ip').value,
            vendor: document.getElementById('device-vendor').value,
            description: document.getElementById('device-description').value,
            isTrusted: document.getElementById('device-is-trusted').checked
        };

        try {
            await this.api('devices', { method: 'POST', body: JSON.stringify(data) });
            document.getElementById('device-details-modal').classList.remove('active');
            this.showToast({ title: 'Succes', message: 'Appareil ajoute', severity: 0 });
            this.loadDevices();
            
            // Restore save button
            const footer = document.querySelector('#device-details-modal .modal-footer');
            const originalBtn = footer.querySelector('.btn-primary');
            originalBtn.onclick = () => this.saveDeviceDetails();
        } catch (error) {
            alert('Erreur: ' + error.message);
        }
    }

    showInstallAgentModal() {
        document.getElementById('install-agent-modal').classList.add('active');
        document.getElementById('install-script-container').style.display = 'none';
    }

    async getInstallScript(os) {
        try {
            // Use the correct route defined in AgentsController: api/agents/install/{platform}
            // Also, since the controller returns raw text (ContentResult), we need to handle it.
            // However, the api() method returns {} if not JSON.
            // Let's use fetch directly here to handle text response, or modify api() to handle text.
            // But wait, the previous plan was to modify controller to return JSON.
            // Let's stick to modifying JS to match the route first.
            
            // Actually, let's use a direct fetch to get the text content, as the controller returns raw script.
            const response = await fetch(`/api/agents/install/${os}`);
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const script = await response.text();
            
            document.getElementById('install-command').textContent = script;
            document.getElementById('install-script-container').style.display = 'block';
        } catch (error) {
            alert('Erreur: ' + error.message);
        }
    }

    async copyInstallCommand() {
        const text = document.getElementById('install-command').textContent;
        await this.copyToClipboard(text);
    }

    clearScanLogs() {
        document.getElementById('scan-logs').innerHTML = '';
    }

    async loadScanLogs() {
        try {
            const logs = await this.api('cameras/scan/logs');
            const container = document.getElementById('scan-logs');
            
            if (logs && logs.length > 0) {
                container.innerHTML = logs.map(log => `
                    <div class="scan-log-entry ${log.level.toLowerCase()}">
                        <span class="scan-log-time">${new Date(log.timestamp).toLocaleTimeString()}</span>
                        <span class="scan-log-message">${this.escapeHtml(log.message)}</span>
                    </div>
                `).join('');
                container.scrollTop = container.scrollHeight;
            }
            
            // Stop if scan is finished (you might need a flag from backend)
        } catch (error) {
            console.error(error);
        }
    }

    // Pi-hole Management
    async loadPihole() {
        try {
            const status = await this.api('pihole/status');
            this.updatePiholeUI(status);
            
            if (status.isInstalled && status.isRunning) {
                await this.loadPiholeStats();
            }
        } catch (error) {
            console.error('Error loading Pi-hole:', error);
        }
    }

    updatePiholeUI(status) {
        const notLinux = document.getElementById('pihole-not-linux');
        const notInstalled = document.getElementById('pihole-not-installed');
        const installed = document.getElementById('pihole-installed');
        
        // Hide all sections first
        notLinux.style.display = 'none';
        notInstalled.style.display = 'none';
        installed.style.display = 'none';
        
        // Show appropriate section
        if (!status.isLinux) {
            notLinux.style.display = 'block';
            return;
        }
        
        if (!status.isInstalled) {
            notInstalled.style.display = 'block';
            return;
        }
        
        // Pi-hole is installed
        installed.style.display = 'block';
        
        // Update status card
        const statusText = document.getElementById('pihole-status-text');
        const statusCard = document.getElementById('pihole-status-card');
        const blockingText = document.getElementById('pihole-blocking-text');
        const blockingCard = document.getElementById('pihole-blocking-card');
        
        if (status.isRunning) {
            statusText.textContent = 'Actif';
            statusCard.style.color = 'var(--success)';
        } else {
            statusText.textContent = 'Arrêté';
            statusCard.style.color = 'var(--danger)';
        }
        
        // Update blocking status
        const btnEnable = document.getElementById('btn-enable-pihole');
        const btnDisable = document.getElementById('btn-disable-pihole');
        
        if (status.isEnabled) {
            blockingText.textContent = 'Activé';
            blockingCard.style.color = 'var(--success)';
            btnEnable.style.display = 'none';
            btnDisable.style.display = 'inline-block';
        } else {
            blockingText.textContent = 'Désactivé';
            blockingCard.style.color = 'var(--warning)';
            btnEnable.style.display = 'inline-block';
            btnDisable.style.display = 'none';
        }
        
        // Update version
        if (status.version) {
            document.getElementById('pihole-version').textContent = status.version;
        }
        
        // Update web URL
        if (status.webUrl) {
            document.getElementById('btn-open-pihole').href = status.webUrl;
        }
    }

    async loadPiholeStats() {
        try {
            const stats = await this.api('pihole/summary', { suppressErrorLog: true });
            
            if (!stats || stats.status === 'unavailable') {
                document.getElementById('pihole-stats-container').style.display = 'none';
                return;
            }

            document.getElementById('pihole-stats-container').style.display = 'block';
            
            document.getElementById('ph-queries').textContent = this.formatNumber(stats.dnsQueriesToday);
            document.getElementById('ph-blocked').textContent = this.formatNumber(stats.adsBlockedToday);
            document.getElementById('ph-percent').textContent = (stats.adsPercentageToday || 0).toFixed(1) + '%';
            document.getElementById('ph-domains').textContent = this.formatNumber(stats.domainsBeingBlocked);
            document.getElementById('ph-clients').textContent = stats.uniqueClients || 0;
            document.getElementById('ph-reply-ip').textContent = this.formatNumber(stats.replyIp);
            document.getElementById('ph-reply-nx').textContent = this.formatNumber(stats.replyNxdomain);
            
            if (stats.gravityLastUpdated && stats.gravityLastUpdated.relative) {
                const rel = stats.gravityLastUpdated.relative;
                let gravityText = '';
                if (rel.days > 0) gravityText = `Il y a ${rel.days}j`;
                else if (rel.hours > 0) gravityText = `Il y a ${rel.hours}h`;
                else gravityText = `Il y a ${rel.minutes}min`;
                document.getElementById('ph-gravity').textContent = gravityText;
            }

        } catch (error) {
            console.error('Error loading Pi-hole stats:', error);
            document.getElementById('pihole-stats-container').style.display = 'none';
        }
    }

    async installPihole() {
        if (!confirm('Installer Pi-hole ? Cette opération peut prendre plusieurs minutes.')) return;
        
        try {
            await this.api('pihole/install', { method: 'POST' });
            this.showToast({ title: 'Installation', message: 'Installation de Pi-hole démarrée', severity: 0 });
            
            // Show progress panel
            document.getElementById('pihole-install-progress').style.display = 'block';
            
            // Poll for logs
            this.piholeLogInterval = setInterval(() => this.pollPiholeLogs(), 2000);
            
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async pollPiholeLogs() {
        try {
            const result = await this.api('pihole/logs');
            const logsContainer = document.getElementById('pihole-install-logs');
            if (logsContainer && result.logs) {
                logsContainer.textContent = result.logs;
                logsContainer.scrollTop = logsContainer.scrollHeight;
                
                // Check if installation is complete
                if (result.logs.includes('=== Installation terminée')) {
                    clearInterval(this.piholeLogInterval);
                    this.showToast({ title: 'Succès', message: 'Pi-hole installé avec succès!', severity: 0 });
                    setTimeout(() => this.loadPihole(), 2000);
                }
            }
        } catch (error) {
            console.error('Error polling Pi-hole logs:', error);
        }
    }

    async uninstallPihole() {
        if (!confirm('Êtes-vous sûr de vouloir désinstaller Pi-hole ? Cette action est irréversible.')) return;
        
        try {
            await this.api('pihole/uninstall', { method: 'POST' });
            this.showToast({ title: 'Désinstallation', message: 'Pi-hole a été désinstallé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async enablePihole() {
        try {
            await this.api('pihole/enable', { method: 'POST' });
            this.showToast({ title: 'Pi-hole', message: 'Blocage activé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    showDisablePiholeModal() {
        document.getElementById('modal-title').textContent = 'Désactiver Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <p>Choisissez la durée de désactivation du blocage :</p>
            <div class="form-group" style="margin-top: 15px;">
                <select id="pihole-disable-duration" class="form-control">
                    <option value="0">Indéfiniment</option>
                    <option value="30">30 secondes</option>
                    <option value="60">1 minute</option>
                    <option value="300">5 minutes</option>
                    <option value="600">10 minutes</option>
                    <option value="1800">30 minutes</option>
                    <option value="3600">1 heure</option>
                </select>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-warning" onclick="app.disablePihole()">Désactiver</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async disablePihole() {
        const duration = parseInt(document.getElementById('pihole-disable-duration').value);
        
        try {
            await this.api('pihole/disable', { 
                method: 'POST',
                body: JSON.stringify({ duration: duration || null })
            });
            
            const msg = duration > 0 
                ? `Blocage désactivé pour ${duration} secondes` 
                : 'Blocage désactivé';
            this.showToast({ title: 'Pi-hole', message: msg, severity: 0 });
            this.closeModal();
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    showPiholePasswordModal() {
        document.getElementById('modal-title').textContent = 'Changer le mot de passe Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nouveau mot de passe</label>
                <input type="password" id="pihole-pwd" class="form-control" placeholder="Laissez vide pour supprimer le mot de passe">
            </div>
            <p style="font-size: 0.85em; color: var(--text-secondary); margin-top: 10px;">
                Ce mot de passe sera utilisé pour accéder à l'interface web de Pi-hole.
            </p>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.setPiholePassword()">Enregistrer</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async setPiholePassword() {
        const pwd = document.getElementById('pihole-pwd').value;
        
        try {
            await this.api('pihole/password', { 
                method: 'POST', 
                body: JSON.stringify({ password: pwd }) 
            });
            this.closeModal();
            this.showToast({ title: 'Succès', message: 'Mot de passe mis à jour', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async scanNetwork() {
        try {
            document.getElementById('scan-btn').disabled = true;
            document.getElementById('scan-btn').innerHTML = `${Icons.spinner} Scan en cours...`;
            
            await this.api('system/scan', { method: 'POST' });
            this.showToast({ title: 'Scan reseau', message: 'Scan initie avec succes', severity: 0 });
            
            setTimeout(() => this.loadPageData(this.currentPage), 5000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        } finally {
            document.getElementById('scan-btn').disabled = false;
            document.getElementById('scan-btn').innerHTML = `${Icons.search} Scanner le reseau`;
        }
    }

    // Toast Notifications
    showToast(alert) {
        const container = document.getElementById('toast-container');
        const toast = document.createElement('div');
        toast.className = `toast ${this.getSeverityClass(alert.severity)}`;
        toast.innerHTML = `
            <div class="toast-title">${this.getAlertTypeIcon(alert.type)} ${this.escapeHtml(alert.title)}</div>
            <div class="toast-message">${this.escapeHtml(alert.message || '')}</div>
        `;
        container.appendChild(toast);
        setTimeout(() => { toast.style.opacity = '0'; setTimeout(() => toast.remove(), 300); }, 5000);
    }

    // Modal
    closeModal() {
        document.getElementById('modal').classList.remove('active');
    }

    // Auto Refresh
    startAutoRefresh() {
        setInterval(() => { if (this.currentPage === 'dashboard') this.loadDashboard(); }, 30000);
    }

    // Helpers
    getSeverityClass(severity) {
        return ['info', 'low', 'medium', 'high', 'critical'][severity] || 'info';
    }

    getStatusClass(status) {
        return ['unknown', 'online', 'offline', 'blocked'][status] || 'unknown';
    }

    getStatusText(status) {
        return ['Inconnu', 'En ligne', 'Hors ligne', 'Bloque'][status] || 'Inconnu';
    }

    getAlertTypeIcon(type) {
        const icons = {
            0: Icons.newDevice, 1: Icons.unknown, 2: Icons.warning, 3: Icons.portScan,
            4: Icons.arpSpoof, 5: Icons.network, 6: Icons.traffic, 7: Icons.danger,
            8: Icons.lock, 9: Icons.arpSpoof
        };
        return icons[type] || Icons.bell;
    }

    formatDate(dateStr) {
        if (!dateStr) return 'N/A';
        const date = new Date(dateStr);
        const now = new Date();
        const diff = now - date;
        
        if (diff < 60000) return 'A l\'instant';
        if (diff < 3600000) return `Il y a ${Math.floor(diff / 60000)} min`;
        if (diff < 86400000) return `Il y a ${Math.floor(diff / 3600000)} h`;
        
        return date.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' });
    }

    formatNumber(num) {
        if (num === undefined || num === null) return '0';
        if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
        if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
        return num.toString();
    }

    formatBytes(bytes) {
        if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB';
        if (bytes >= 1048576) return (bytes / 1048576).toFixed(2) + ' MB';
        if (bytes >= 1024) return (bytes / 1024).toFixed(2) + ' KB';
        return bytes + ' B';
    }

    escapeHtml(str) {
        if (!str) return '';
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return str.toString().replace(/[&<>"']/g, m => map[m]);
    }
}

// Initialize app
const app = new FirewallApp();
