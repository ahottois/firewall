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
        this.init();
    }

    init() {
        this.setupNavigation();
        this.setupEventListeners();
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

    navigateTo(page) {
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
            settings: 'Parametres',
            admin: 'Administration'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;
        this.loadPageData(page);
    }

    loadPageData(page) {
        switch (page) {
            case 'dashboard': this.loadDashboard(); break;
            case 'devices': this.loadDevices(); break;
            case 'cameras': this.loadCameras(); break;
            case 'alerts': this.loadAlerts(); break;
            case 'traffic': this.loadTraffic(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }

    setupEventListeners() {
        document.getElementById('scan-btn').addEventListener('click', () => this.scanNetwork());
        document.getElementById('mark-all-read').addEventListener('click', () => this.markAllAlertsRead());
        
        document.getElementById('scan-cameras-btn')?.addEventListener('click', () => this.scanCameras());
        document.getElementById('add-camera-btn')?.addEventListener('click', () => this.showAddCameraModal());
        
        document.querySelectorAll('.filter-buttons .btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.filter-buttons .btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.loadDevices(e.target.dataset.filter);
            });
        });

        document.querySelector('.modal-close').addEventListener('click', () => this.closeModal());
        document.getElementById('modal').addEventListener('click', (e) => {
            if (e.target.id === 'modal') this.closeModal();
        });
        
        document.getElementById('camera-modal').addEventListener('click', (e) => {
            if (e.target.id === 'camera-modal') this.closeCameraModal();
        });
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
            console.error(`API Error (${endpoint}):`, error);
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

    // Dashboard
    async loadDashboard() {
        try {
            const [devices, unknownDevices, alerts, unreadCount, stats, status, cameras, vulnerableCameras] = await Promise.all([
                this.api('devices'),
                this.api('devices/unknown'),
                this.api('alerts?count=10'),
                this.api('alerts/unread/count'),
                this.api('traffic/stats?hours=1'),
                this.api('system/status'),
                this.api('cameras').catch(() => []),
                this.api('cameras/vulnerable').catch(() => [])
            ]);

            // Update stats
            document.getElementById('total-devices').textContent = devices.length;
            document.getElementById('online-devices').textContent = devices.filter(d => d.status === 1).length;
            document.getElementById('unknown-devices').textContent = unknownDevices.length;
            document.getElementById('unread-alerts').textContent = unreadCount;
            
            // Camera stats
            document.getElementById('total-cameras').textContent = cameras.length;
            document.getElementById('vulnerable-cameras').textContent = vulnerableCameras.length;

            // Update alert badge
            this.updateAlertBadgeValue(unreadCount);
            
            // Update camera badge
            if (vulnerableCameras.length > 0) {
                document.getElementById('camera-badge').textContent = vulnerableCameras.length;
                document.getElementById('camera-badge').style.display = 'inline';
            } else {
                document.getElementById('camera-badge').style.display = 'none';
            }

            // Update capture status
            const statusDot = document.getElementById('capture-status');
            const statusText = document.getElementById('capture-text');
            if (status.isCapturing) {
                statusDot.classList.add('active');
                statusText.textContent = 'Capture active';
            } else {
                statusDot.classList.remove('active');
                statusText.textContent = 'Capture inactive';
            }

            // Render recent alerts
            this.renderRecentAlerts(alerts);

            // Render unknown devices
            this.renderUnknownDevices(unknownDevices.slice(0, 5));

            // Render traffic stats
            this.renderTrafficStats(stats);
        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    renderRecentAlerts(alerts) {
        const container = document.getElementById('recent-alerts');
        if (!alerts.length) {
            container.innerHTML = `<div class="empty-state"><div class="empty-state-icon">${Icons.checkCircle}</div><p>Aucune alerte recente</p></div>`;
            return;
        }

        container.innerHTML = alerts.slice(0, 5).map(alert => `
            <div class="alert-item ${this.getSeverityClass(alert.severity)} ${alert.isRead ? '' : 'unread'}">
                <div class="alert-content">
                    <div class="alert-title">${this.escapeHtml(alert.title)}</div>
                    <div class="alert-message">${this.escapeHtml(alert.message)}</div>
                    <div class="alert-time">${this.formatDate(alert.timestamp)}</div>
                </div>
            </div>
        `).join('');
    }

    renderUnknownDevices(devices) {
        const container = document.getElementById('unknown-devices-list');
        if (!devices.length) {
            container.innerHTML = `<div class="empty-state"><div class="empty-state-icon">${Icons.checkCircle}</div><p>Tous les appareils sont connus</p></div>`;
            return;
        }

        container.innerHTML = devices.map(device => `
            <div class="device-item">
                <div class="device-info">
                    <div class="device-mac">${this.escapeHtml(device.macAddress)}</div>
                    <div class="device-ip">${this.escapeHtml(device.ipAddress || 'N/A')} - ${this.escapeHtml(device.vendor || 'Unknown')}</div>
                </div>
                <button class="btn btn-sm btn-success" onclick="app.trustDevice(${device.id})">${Icons.check} Approuver</button>
            </div>
        `).join('');
    }

    renderTrafficStats(stats) {
        const container = document.getElementById('traffic-stats');
        container.innerHTML = `
            <div class="stat-item"><div class="stat-label">Paquets Total</div><div class="stat-value">${this.formatNumber(stats.totalPackets)}</div></div>
            <div class="stat-item"><div class="stat-label">Donnees</div><div class="stat-value">${this.formatBytes(stats.totalBytes)}</div></div>
            <div class="stat-item"><div class="stat-label">Appareils Uniques</div><div class="stat-value">${stats.uniqueDevices}</div></div>
            <div class="stat-item"><div class="stat-label">Suspects</div><div class="stat-value">${stats.suspiciousPackets}</div></div>
        `;
    }

    // Devices
    async loadDevices(filter = 'all') {
        try {
            let devices;
            switch (filter) {
                case 'online': devices = await this.api('devices/online'); break;
                case 'unknown': devices = await this.api('devices/unknown'); break;
                default: devices = await this.api('devices');
            }

            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    }

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil trouve</td></tr>';
            return;
        }

        tbody.innerHTML = devices.map(device => `
            <tr>
                <td><span class="status-badge ${this.getStatusClass(device.status)}">${this.getStatusText(device.status)}</span></td>
                <td><code>${this.escapeHtml(device.macAddress)}</code></td>
                <td>${this.escapeHtml(device.ipAddress || 'N/A')}</td>
                <td>${this.escapeHtml(device.vendor || 'Unknown')}</td>
                <td>${this.escapeHtml(device.description || '-')}</td>
                <td>${this.formatDate(device.lastSeen)}</td>
                <td>
                    <div class="alert-actions">
                        ${!device.isKnown ? `<button class="btn btn-sm btn-success" onclick="app.trustDevice(${device.id})">${Icons.check}</button>` : ''}
                        <button class="btn btn-sm" onclick="app.showDeviceDetails(${device.id})">${Icons.eye}</button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteDevice(${device.id})">${Icons.times}</button>
                    </div>
                </td>
            </tr>
        `).join('');
    }

    async trustDevice(id) {
        try {
            await this.api(`devices/${id}/trust`, { method: 'POST', body: JSON.stringify({ trusted: true }) });
            this.showToast({ title: 'Appareil approuve', severity: 0 });
            this.loadPageData(this.currentPage);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async deleteDevice(id) {
        if (!confirm('Supprimer cet appareil ?')) return;
        try {
            await this.api(`devices/${id}`, { method: 'DELETE' });
            this.loadPageData(this.currentPage);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async showDeviceDetails(id) {
        const device = await this.api(`devices/${id}`);
        document.getElementById('modal-title').textContent = 'Details Appareil';
        document.getElementById('modal-body').innerHTML = `
            <div class="settings-info">
                <div class="settings-item"><div class="settings-label">MAC Address</div><div class="settings-value"><code>${this.escapeHtml(device.macAddress)}</code></div></div>
                <div class="settings-item"><div class="settings-label">IP Address</div><div class="settings-value">${this.escapeHtml(device.ipAddress || 'N/A')}</div></div>
                <div class="settings-item"><div class="settings-label">Fabricant</div><div class="settings-value">${this.escapeHtml(device.vendor || 'Unknown')}</div></div>
                <div class="settings-item"><div class="settings-label">Premiere Vue</div><div class="settings-value">${this.formatDate(device.firstSeen)}</div></div>
                <div class="settings-item"><div class="settings-label">Derniere Vue</div><div class="settings-value">${this.formatDate(device.lastSeen)}</div></div>
                <div class="settings-item"><div class="settings-label">Status</div><div class="settings-value">${device.isKnown ? Icons.checkCircle + ' Connu' : Icons.warning + ' Inconnu'} ${device.isTrusted ? '- ' + Icons.lock + ' Approuve' : ''}</div></div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `<button class="btn btn-sm" onclick="app.closeModal()">Fermer</button>`;
        document.getElementById('modal').classList.add('active');
    }

    // Cameras
    async loadCameras() {
        try {
            const [cameras, vulnerableCameras] = await Promise.all([
                this.api('cameras'),
                this.api('cameras/vulnerable')
            ]);

            // Update stats
            document.getElementById('cameras-total').textContent = cameras.length;
            document.getElementById('cameras-online').textContent = cameras.filter(c => c.status === 1 || c.status === 3).length;
            document.getElementById('cameras-vulnerable').textContent = vulnerableCameras.length;
            document.getElementById('cameras-secured').textContent = cameras.filter(c => c.passwordStatus === 2).length;

            this.renderCamerasGrid(cameras);
            
            // Load existing scan logs
            this.loadScanLogs();
        } catch (error) {
            console.error('Error loading cameras:', error);
            document.getElementById('cameras-grid').innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">${Icons.video}</div>
                    <p>Aucune camera detectee</p>
                    <button class="btn btn-primary" onclick="app.scanCameras()">${Icons.search} Scanner les cameras</button>
                </div>
            `;
        }
    }

    renderCamerasGrid(cameras) {
        const container = document.getElementById('cameras-grid');
        
        if (!cameras.length) {
            container.innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">${Icons.video}</div>
                    <p>Aucune camera detectee</p>
                    <p style="font-size: 0.85rem; color: var(--text-secondary)">Cliquez sur "Scanner les cameras" pour rechercher des cameras sur votre reseau</p>
                </div>
            `;
            return;
        }

        container.innerHTML = cameras.map(camera => `
            <div class="camera-card ${this.getCameraCardClass(camera)}">
                <div class="camera-preview" onclick="app.viewCamera(${camera.id})">
                    <div class="camera-preview-placeholder">
                        ${Icons.video}
                        <p>Cliquez pour visualiser</p>
                    </div>
                    <div class="camera-status-overlay">
                        ${this.getCameraStatusBadge(camera)}
                        ${this.getPasswordStatusBadge(camera)}
                    </div>
                </div>
                <div class="camera-info">
                    <div class="camera-name">${camera.manufacturer || 'Camera'} ${camera.model || ''}</div>
                    <div class="camera-address">${camera.ipAddress}:${camera.port}</div>
                    <div class="camera-meta">
                        <span>${Icons.calendar} ${this.formatDate(camera.firstDetected)}</span>
                        <span>${Icons.sync} ${this.formatDate(camera.lastChecked)}</span>
                    </div>
                    <div class="camera-password-status ${this.getPasswordStatusClass(camera.passwordStatus)}">
                        ${this.getPasswordStatusText(camera)}
                    </div>
                    <div class="camera-actions">
                        <button class="btn btn-sm btn-primary" onclick="app.viewCamera(${camera.id})">${Icons.eye} Voir</button>
                        <button class="btn btn-sm" onclick="app.checkCamera(${camera.id})">${Icons.sync} Verifier</button>
                        <button class="btn btn-sm" onclick="app.testCameraCredentials(${camera.id})">${Icons.key} Tester</button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteCamera(${camera.id})">${Icons.times}</button>
                    </div>
                </div>
            </div>
        `).join('');
    }

    getCameraCardClass(camera) {
        if (camera.passwordStatus === 1 || camera.passwordStatus === 3) return 'vulnerable';
        if (camera.passwordStatus === 2) return 'secured';
        return '';
    }

    getCameraStatusBadge(camera) {
        const statusMap = {
            0: `<span class="camera-status-badge">${Icons.unknown} Inconnu</span>`,
            1: `<span class="camera-status-badge online">${Icons.online} En ligne</span>`,
            2: `<span class="camera-status-badge offline">${Icons.offline} Hors ligne</span>`,
            3: `<span class="camera-status-badge online">${Icons.check} Authentifie</span>`,
            4: `<span class="camera-status-badge">${Icons.lock} Auth requise</span>`
        };
        return statusMap[camera.status] || '';
    }

    getPasswordStatusBadge(camera) {
        if (camera.passwordStatus === 1) return `<span class="camera-status-badge vulnerable">${Icons.warning} MDP DEFAUT</span>`;
        if (camera.passwordStatus === 3) return `<span class="camera-status-badge vulnerable">${Icons.unlock} SANS MDP</span>`;
        if (camera.passwordStatus === 2) return `<span class="camera-status-badge secured">${Icons.lock} Securisee</span>`;
        return '';
    }

    getPasswordStatusClass(status) {
        return { 0: 'unknown', 1: 'default', 2: 'custom', 3: 'nopassword', 4: 'unknown' }[status] || 'unknown';
    }

    getPasswordStatusText(camera) {
        const texts = {
            0: `${Icons.unknown} Status mot de passe inconnu`,
            1: `${Icons.warning} MOT DE PASSE PAR DEFAUT DETECTE! (${camera.detectedCredentials || 'admin:admin'})`,
            2: `${Icons.checkCircle} Mot de passe personnalise`,
            3: `${Icons.unlock} AUCUN MOT DE PASSE!`,
            4: `${Icons.lock} Mot de passe requis`
        };
        return texts[camera.passwordStatus] || 'Inconnu';
    }

    // Camera Scan with real-time logs
    async scanCameras() {
        try {
            document.getElementById('scan-cameras-btn').disabled = true;
            document.getElementById('scan-cameras-btn').innerHTML = `${Icons.spinner} Scan en cours...`;
            
            // Show scan logs panel
            const logsPanel = document.getElementById('scan-logs-panel');
            if (logsPanel) {
                logsPanel.style.display = 'block';
            }
            
            // Clear previous logs
            this.clearScanLogs();
            
            // Connect to SSE for real-time logs
            this.connectScanLogs();
            
            this.showToast({ title: 'Scan des cameras', message: 'Recherche en cours, suivez la progression ci-dessous...', severity: 0 });
            
            const cameras = await this.api('cameras/scan', { method: 'POST' });
            
            this.showToast({ 
                title: 'Scan termine', 
                message: `${cameras.length} camera(s) detectee(s)`, 
                severity: cameras.some(c => c.passwordStatus === 1 || c.passwordStatus === 3) ? 3 : 0 
            });
            
            this.loadCameras();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
            this.addScanLog({ message: `? Erreur: ${error.message}`, level: 'error' });
        } finally {
            document.getElementById('scan-cameras-btn').disabled = false;
            document.getElementById('scan-cameras-btn').innerHTML = `${Icons.search} Scanner les cameras`;
        }
    }

    // Real-time scan logs via SSE
    connectScanLogs() {
        if (this.scanLogSource) {
            this.scanLogSource.close();
        }

        this.scanLogSource = new EventSource('/api/cameras/scan/logs/stream');

        this.scanLogSource.addEventListener('log', (e) => {
            const log = JSON.parse(e.data);
            this.addScanLog(log);
        });

        this.scanLogSource.onerror = () => {
            console.log('Scan log stream disconnected');
        };
    }

    disconnectScanLogs() {
        if (this.scanLogSource) {
            this.scanLogSource.close();
            this.scanLogSource = null;
        }
    }

    addScanLog(log) {
        const logsContainer = document.getElementById('scan-logs');
        if (!logsContainer) return;

        const logEntry = document.createElement('div');
        logEntry.className = `scan-log-entry ${log.level || 'info'}`;
        
        const time = log.timestamp ? new Date(log.timestamp).toLocaleTimeString() : new Date().toLocaleTimeString();
        
        let progressHtml = '';
        if (log.progress) {
            progressHtml = `
                <div class="scan-progress">
                    <div class="scan-progress-bar" style="width: ${log.progress.percentage}%"></div>
                    <span class="scan-progress-text">${log.progress.current}/${log.progress.total} (${log.progress.percentage}%)</span>
                </div>
            `;
        }

        logEntry.innerHTML = `
            <span class="scan-log-time">[${time}]</span>
            <span class="scan-log-message">${this.escapeHtml(log.message)}</span>
            ${progressHtml}
        `;

        logsContainer.appendChild(logEntry);
        logsContainer.scrollTop = logsContainer.scrollHeight;
    }

    clearScanLogs() {
        const logsContainer = document.getElementById('scan-logs');
        if (logsContainer) {
            logsContainer.innerHTML = '';
        }
    }

    async loadScanLogs() {
        try {
            const logs = await this.api('cameras/scan/logs?count=50');
            const logsContainer = document.getElementById('scan-logs');
            if (logsContainer && logs.length > 0) {
                document.getElementById('scan-logs-panel').style.display = 'block';
                logs.reverse().forEach(log => this.addScanLog(log));
            }
        } catch (error) {
            console.log('No scan logs available');
        }
    }

    async viewCamera(id) {
        this.currentCameraId = id;
        const camera = await this.api(`cameras/${id}`);
        
        document.getElementById('camera-modal-title').textContent = `${camera.manufacturer || 'Camera'} - ${camera.ipAddress}`;
        document.getElementById('camera-feed').innerHTML = `<div class="camera-placeholder">${Icons.spinner}<p>Chargement du snapshot...</p></div>`;
        
        document.getElementById('camera-details').innerHTML = `
            <div class="camera-detail-item"><div class="camera-detail-label">Adresse IP</div><div class="camera-detail-value">${camera.ipAddress}:${camera.port}</div></div>
            <div class="camera-detail-item"><div class="camera-detail-label">Fabricant</div><div class="camera-detail-value">${camera.manufacturer || 'Inconnu'}</div></div>
            <div class="camera-detail-item"><div class="camera-detail-label">Status Mot de Passe</div><div class="camera-detail-value ${camera.passwordStatus === 1 || camera.passwordStatus === 3 ? 'danger' : camera.passwordStatus === 2 ? 'success' : ''}">${this.getPasswordStatusText(camera)}</div></div>
            <div class="camera-detail-item"><div class="camera-detail-label">Identifiants Detectes</div><div class="camera-detail-value ${camera.passwordStatus === 1 ? 'danger' : ''}">${camera.detectedCredentials || 'Aucun'}</div></div>
            <div class="camera-detail-item"><div class="camera-detail-label">URL Stream</div><div class="camera-detail-value" style="font-size: 0.8rem; word-break: break-all;">${camera.streamUrl || 'Non disponible'}</div></div>
            <div class="camera-detail-item"><div class="camera-detail-label">Premiere Detection</div><div class="camera-detail-value">${this.formatDate(camera.firstDetected)}</div></div>
        `;
        
        document.getElementById('camera-modal').classList.add('active');
        
        // Charger le snapshot
        await this.refreshCameraSnapshot();
    }

    async refreshCameraSnapshot() {
        if (!this.currentCameraId) return;
        
        document.getElementById('camera-feed').innerHTML = `<div class="camera-placeholder">${Icons.spinner}<p>Chargement...</p></div>`;
        
        try {
            const result = await this.api(`cameras/${this.currentCameraId}/snapshot`);
            if (result.image) {
                this.currentSnapshot = result.image;
                document.getElementById('camera-feed').innerHTML = `<img src="data:image/jpeg;base64,${result.image}" alt="Camera snapshot">`;
            } else {
                throw new Error('No image');
            }
        } catch (error) {
            document.getElementById('camera-feed').innerHTML = `<div class="camera-placeholder">${Icons.video}<p>Impossible de charger l'image</p><p style="font-size: 0.8rem;">La camera peut necessiter une authentification ou etre hors ligne</p></div>`;
        }
    }

    downloadSnapshot() {
        if (!this.currentSnapshot) {
            this.showToast({ title: 'Erreur', message: 'Aucune image a telecharger', severity: 2 });
            return;
        }
        const link = document.createElement('a');
        link.href = `data:image/jpeg;base64,${this.currentSnapshot}`;
        link.download = `camera_${this.currentCameraId}_${Date.now()}.jpg`;
        link.click();
    }

    closeCameraModal() {
        document.getElementById('camera-modal').classList.remove('active');
        this.currentCameraId = null;
        this.currentSnapshot = null;
    }

    async checkCamera(id) {
        try {
            this.showToast({ title: 'Verification', message: 'Verification de la camera en cours...', severity: 0 });
            await this.api(`cameras/${id}/check`, { method: 'POST' });
            this.loadCameras();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async testCameraCredentials(id) {
        const camera = await this.api(`cameras/${id}`);
        
        document.getElementById('modal-title').textContent = 'Tester les identifiants';
        document.getElementById('modal-body').innerHTML = `
            <div class="add-camera-form">
                <p>Tester des identifiants personnalises pour la camera ${camera.ipAddress}:${camera.port}</p>
                <div class="form-group"><label>Nom d'utilisateur</label><input type="text" id="test-username" value="admin" placeholder="admin"></div>
                <div class="form-group"><label>Mot de passe</label><input type="password" id="test-password" placeholder="Mot de passe"></div>
                <div id="test-result"></div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.doTestCredentials(${id})">${Icons.key} Tester</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async doTestCredentials(id) {
        const username = document.getElementById('test-username').value;
        const password = document.getElementById('test-password').value;
        
        document.getElementById('test-result').innerHTML = `<p>${Icons.spinner} Test en cours...</p>`;
        
        try {
            const result = await this.api(`cameras/${id}/test-credentials`, {
                method: 'POST',
                body: JSON.stringify({ username, password })
            });
            
            document.getElementById('test-result').innerHTML = result.success 
                ? `<div class="camera-password-status custom">${Icons.checkCircle} Identifiants valides!</div>`
                : `<div class="camera-password-status default">${Icons.timesCircle} Identifiants invalides</div>`;
        } catch (error) {
            document.getElementById('test-result').innerHTML = `<div class="camera-password-status default">${Icons.timesCircle} Erreur: ${error.message}</div>`;
        }
    }

    showAddCameraModal() {
        document.getElementById('modal-title').textContent = 'Ajouter une camera manuellement';
        document.getElementById('modal-body').innerHTML = `
            <div class="add-camera-form">
                <div class="form-row">
                    <div class="form-group"><label>Adresse IP</label><input type="text" id="add-camera-ip" placeholder="192.168.1.100"></div>
                    <div class="form-group"><label>Port</label><input type="number" id="add-camera-port" value="80" placeholder="80"></div>
                </div>
                <div id="add-camera-result"></div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Annuler</button>
            <button class="btn btn-primary" onclick="app.doAddCamera()">${Icons.search} Verifier et ajouter</button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async doAddCamera() {
        const ip = document.getElementById('add-camera-ip').value;
        const port = parseInt(document.getElementById('add-camera-port').value) || 80;
        
        if (!ip) {
            this.showToast({ title: 'Erreur', message: 'Veuillez entrer une adresse IP', severity: 2 });
            return;
        }
        
        document.getElementById('add-camera-result').innerHTML = `<p>${Icons.spinner} Verification en cours...</p>`;
        
        try {
            const camera = await this.api('cameras/check', {
                method: 'POST',
                body: JSON.stringify({ ipAddress: ip, port })
            });
            
            document.getElementById('add-camera-result').innerHTML = `
                <div class="camera-password-status ${camera.passwordStatus === 1 ? 'default' : 'custom'}">
                    ${Icons.checkCircle} Camera ajoutee: ${camera.manufacturer || 'Inconnue'}<br>
                    ${this.getPasswordStatusText(camera)}
                </div>
            `;
            
            setTimeout(() => { this.closeModal(); this.loadCameras(); }, 2000);
        } catch (error) {
            document.getElementById('add-camera-result').innerHTML = `<div class="camera-password-status default">${Icons.timesCircle} Aucune camera detectee a cette adresse</div>`;
        }
    }

    async deleteCamera(id) {
        if (!confirm('Supprimer cette camera ?')) return;
        try {
            await this.api(`cameras/${id}`, { method: 'DELETE' });
            this.loadCameras();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    // Alerts
    async loadAlerts() {
        try {
            const alerts = await this.api('alerts?count=100');
            this.renderAlertsList(alerts);
        } catch (error) {
            console.error('Error loading alerts:', error);
        }
    }

    renderAlertsList(alerts) {
        const container = document.getElementById('alerts-list');
        if (!alerts.length) {
            container.innerHTML = `<div class="empty-state"><div class="empty-state-icon">${Icons.checkCircle}</div><p>Aucune alerte</p></div>`;
            return;
        }

        container.innerHTML = alerts.map(alert => `
            <div class="alert-item ${this.getSeverityClass(alert.severity)} ${alert.isRead ? '' : 'unread'} ${alert.isResolved ? 'resolved' : ''}">
                <div class="alert-content">
                    <div class="alert-title">${this.getAlertTypeIcon(alert.type)} ${this.escapeHtml(alert.title)}</div>
                    <div class="alert-message">${this.escapeHtml(alert.message)}</div>
                    <div class="alert-time">${this.formatDate(alert.timestamp)}${alert.sourceIp ? ` - Source: ${this.escapeHtml(alert.sourceIp)}` : ''}${alert.protocol ? ` - ${this.escapeHtml(alert.protocol)}` : ''}</div>
                </div>
                <div class="alert-actions">
                    ${!alert.isRead ? `<button class="btn btn-sm" onclick="app.markAlertRead(${alert.id})" title="Marquer comme lu">${Icons.check}</button>` : ''}
                    ${!alert.isResolved ? `<button class="btn btn-sm btn-success" onclick="app.resolveAndDeleteAlert(${alert.id})" title="Resoudre et supprimer">${Icons.checkCircle}</button>` : ''}
                    <button class="btn btn-sm btn-danger" onclick="app.deleteAlert(${alert.id})" title="Supprimer">${Icons.trash}</button>
                </div>
            </div>
        `).join('');
    }

    async markAlertRead(id) {
        await this.apiPost(`alerts/${id}/read`);
        this.loadAlerts();
        this.updateAlertBadge();
    }

    async resolveAlert(id) {
        await this.apiPost(`alerts/${id}/resolve`);
        this.loadAlerts();
        this.updateAlertBadge();
    }

    async resolveAndDeleteAlert(id) {
        try {
            await this.api(`alerts/${id}`, { method: 'DELETE' });
            this.loadAlerts();
            this.updateAlertBadge();
            this.showToast({ title: 'Alerte resolue', message: 'L\'alerte a ete supprimee', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async deleteAlert(id) {
        try {
            await this.api(`alerts/${id}`, { method: 'DELETE' });
            this.loadAlerts();
            this.updateAlertBadge();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async markAllAlertsRead() {
        await this.apiPost('alerts/read-all');
        this.loadAlerts();
        this.updateAlertBadge();
    }

    async resolveAllAlerts() {
        if (!confirm('Resoudre et supprimer toutes les alertes ?')) return;
        try {
            await this.api('alerts/all', { method: 'DELETE' });
            this.loadAlerts();
            this.updateAlertBadge();
            this.showToast({ title: 'Alertes resolues', message: 'Toutes les alertes ont ete supprimees', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async resetAllAlerts() {
        if (!confirm('Reinitialiser le systeme d\'alertes ? Cela va:\n- Supprimer toutes les alertes\n- Reinitialiser les cooldowns de notifications\n- Relancer un scan reseau')) return;
        try {
            const result = await this.api('alerts/reset', { method: 'POST' });
            this.loadAlerts();
            this.updateAlertBadge();
            this.showToast({ 
                title: 'Reinitialisation effectuee', 
                message: result.message || 'Le systeme a ete reinitialise', 
                severity: 0 
            });
            
            // Reload dashboard after a delay to show new scan results
            setTimeout(() => {
                if (this.currentPage === 'dashboard') this.loadDashboard();
            }, 5000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        }
    }

    async updateAlertBadge() {
        const count = await this.api('alerts/unread/count');
        this.updateAlertBadgeValue(count);
    }

    updateAlertBadgeValue(count) {
        document.getElementById('alert-badge').textContent = count;
        document.getElementById('notification-count').textContent = count;
        document.getElementById('alert-badge').style.display = count > 0 ? 'inline' : 'none';
        document.getElementById('notification-count').style.display = count > 0 ? 'inline' : 'none';
    }

    // Traffic
    async loadTraffic() {
        try {
            const stats = await this.api('traffic/stats?hours=1');
            
            document.getElementById('total-packets').textContent = this.formatNumber(stats.totalPackets);
            document.getElementById('inbound-packets').textContent = this.formatNumber(stats.inboundPackets);
            document.getElementById('outbound-packets').textContent = this.formatNumber(stats.outboundPackets);
            document.getElementById('suspicious-packets').textContent = stats.suspiciousPackets;

            this.renderProtocols(stats.topProtocols);
        } catch (error) {
            console.error('Error loading traffic:', error);
        }
    }

    renderProtocols(protocols) {
        const container = document.getElementById('protocols-chart');
        const total = Object.values(protocols).reduce((a, b) => a + b, 0) || 1;

        container.innerHTML = Object.entries(protocols).map(([name, count]) => `
            <div class="protocol-item">
                <span>${this.escapeHtml(name)}</span>
                <div class="protocol-bar"><div class="protocol-bar-fill" style="width: ${(count / total) * 100}%"></div></div>
                <span>${this.formatNumber(count)}</span>
            </div>
        `).join('') || '<div class="empty-state">Aucune donnee de protocole</div>';
    }

    // Settings
    async loadSettings() {
        try {
            const [status, interfaces] = await Promise.all([
                this.api('system/status'),
                this.api('system/interfaces')
            ]);

            document.getElementById('system-info').innerHTML = `
                <div class="settings-item"><div class="settings-label">Status Capture</div><div class="settings-value">${status.isCapturing ? Icons.online + ' Active' : Icons.offline + ' Inactive'}</div></div>
                <div class="settings-item"><div class="settings-label">Interface</div><div class="settings-value">${this.escapeHtml(status.currentInterface || 'Non selectionnee')}</div></div>
                <div class="settings-item"><div class="settings-label">Version</div><div class="settings-value">${status.version}</div></div>
                <div class="settings-item"><div class="settings-label">Heure Serveur</div><div class="settings-value">${this.formatDate(status.serverTime)}</div></div>
            `;

            document.getElementById('interfaces-list').innerHTML = interfaces.map(iface => `
                <div class="interface-item">
                    <div>
                        <strong>${this.escapeHtml(iface.name)}</strong>
                        <div class="device-ip">${this.escapeHtml(iface.description)}</div>
                        ${iface.macAddress ? `<div class="device-ip">MAC: ${this.escapeHtml(iface.macAddress)}</div>` : ''}
                    </div>
                    <span class="status-badge ${iface.isUp ? 'online' : 'offline'}">${iface.isUp ? 'Up' : 'Down'}</span>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading settings:', error);
        }
    }

    // Admin Page
    async loadAdmin() {
        try {
            const status = await this.api('admin/status');
            
            document.getElementById('app-version').textContent = status.currentVersion;
            
            const statusCard = document.getElementById('service-status-card');
            const statusText = document.getElementById('service-status-text');
            
            if (!status.isInstalled) {
                statusText.textContent = 'Non installe';
                statusCard.className = 'stat-card not-installed';
            } else if (status.isRunning) {
                statusText.textContent = 'En cours';
                statusCard.className = 'stat-card running';
            } else {
                statusText.textContent = 'Arrete';
                statusCard.className = 'stat-card stopped';
            }

            // Update button states
            document.getElementById('btn-start-service').disabled = !status.isInstalled || status.isRunning;
            document.getElementById('btn-stop-service').disabled = !status.isInstalled || !status.isRunning;
            document.getElementById('btn-restart-service').disabled = !status.isInstalled;
            document.getElementById('btn-uninstall-service').disabled = !status.isInstalled;

            // Check for updates
            this.checkForUpdates();
            
        } catch (error) {
            console.error('Error loading admin:', error);
        }
    }

    async checkForUpdates() {
        const updateStatusEl = document.getElementById('update-status');
        const btnUpdate = document.getElementById('btn-update');
        
        if (updateStatusEl) {
            updateStatusEl.innerHTML = `${Icons.spinner} Verification des mises a jour...`;
            updateStatusEl.className = 'update-status checking';
        }

        try {
            const result = await this.api('admin/check-update');
            
            if (!result.success) {
                if (updateStatusEl) {
                    updateStatusEl.innerHTML = `${Icons.warning} Impossible de verifier: ${result.error}`;
                    updateStatusEl.className = 'update-status error';
                }
                return;
            }

            if (result.updateAvailable) {
                if (updateStatusEl) {
                    updateStatusEl.innerHTML = `
                        ${Icons.info} <strong>Mise a jour disponible!</strong><br>
                        <small>
                            Version locale: <code>${result.localCommit}</code><br>
                            Derniere version: <code>${result.remoteCommit}</code><br>
                            ${result.latestCommitMessage ? `Message: ${this.escapeHtml(result.latestCommitMessage)}<br>` : ''}
                            ${result.latestCommitAuthor ? `Auteur: ${this.escapeHtml(result.latestCommitAuthor)}<br>` : ''}
                            ${result.latestCommitDate ? `Date: ${this.formatDate(result.latestCommitDate)}` : ''}
                        </small>
                    `;
                    updateStatusEl.className = 'update-status available';
                }
                if (btnUpdate) {
                    btnUpdate.classList.add('btn-pulse');
                }
            } else {
                if (updateStatusEl) {
                    updateStatusEl.innerHTML = `${Icons.checkCircle} Vous etes a jour (${result.localCommit})`;
                    updateStatusEl.className = 'update-status uptodate';
                }
                if (btnUpdate) {
                    btnUpdate.classList.remove('btn-pulse');
                }
            }
        } catch (error) {
            if (updateStatusEl) {
                updateStatusEl.innerHTML = `${Icons.warning} Erreur: ${error.message}`;
                updateStatusEl.className = 'update-status error';
            }
        }
    }

    async startService() {
        this.setAdminResult('service-result', 'loading', `${Icons.spinner} Demarrage du service...`);
        try {
            const result = await this.api('admin/service/start', { method: 'POST' });
            this.setAdminResult('service-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} Service demarre` : `${Icons.timesCircle} Erreur: ${result.error}`);
            this.loadAdmin();
        } catch (error) {
            this.setAdminResult('service-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async stopService() {
        this.setAdminResult('service-result', 'loading', `${Icons.spinner} Arret du service...`);
        try {
            const result = await this.api('admin/service/stop', { method: 'POST' });
            this.setAdminResult('service-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} Service arrete` : `${Icons.timesCircle} Erreur: ${result.error}`);
            this.loadAdmin();
        } catch (error) {
            this.setAdminResult('service-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async restartService() {
        this.setAdminResult('service-result', 'loading', `${Icons.spinner} Redemarrage du service...`);
        try {
            const result = await this.api('admin/service/restart', { method: 'POST' });
            this.setAdminResult('service-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} Service redemarre` : `${Icons.timesCircle} Erreur: ${result.error}`);
            setTimeout(() => this.loadAdmin(), 2000);
        } catch (error) {
            this.setAdminResult('service-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async installService() {
        if (!confirm('Installer NetGuard comme service systemd ?')) return;
        
        this.setAdminResult('install-result', 'loading', `${Icons.spinner} Installation du service...`);
        try {
            const result = await this.api('admin/service/install', { method: 'POST' });
            this.setAdminResult('install-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} ${result.output}` : `${Icons.timesCircle} Erreur: ${result.error}`);
            this.loadAdmin();
        } catch (error) {
            this.setAdminResult('install-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async uninstallService() {
        if (!confirm('Desinstaller le service NetGuard ? L\'application continuera a fonctionner mais ne demarrera plus automatiquement.')) return;
        
        this.setAdminResult('install-result', 'loading', `${Icons.spinner} Desinstallation du service...`);
        try {
            const result = await this.api('admin/service/uninstall', { method: 'POST' });
            this.setAdminResult('install-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} ${result.output}` : `${Icons.timesCircle} Erreur: ${result.error}`);
            this.loadAdmin();
        } catch (error) {
            this.setAdminResult('install-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async updateFromGithub() {
        if (!confirm('Mettre a jour depuis GitHub ? L\'application va redemarrer.')) return;
        
        this.setAdminResult('update-result', 'loading', `${Icons.spinner} Mise a jour en cours... Cela peut prendre plusieurs minutes.`);
        document.getElementById('btn-update').disabled = true;
        
        try {
            const result = await this.api('admin/update', { method: 'POST' });
            this.setAdminResult('update-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} ${result.output}` : `${Icons.timesCircle} Erreur: ${result.error}`);
            
            if (result.success) {
                this.setAdminResult('update-result', 'success', `${Icons.checkCircle} Mise a jour terminee. Rechargement dans 5 secondes...`);
                setTimeout(() => location.reload(), 5000);
            }
        } catch (error) {
            this.setAdminResult('update-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        } finally {
            document.getElementById('btn-update').disabled = false;
        }
    }

    async loadServiceLogs() {
        const logsElement = document.getElementById('service-logs');
        logsElement.innerHTML = '<div class="loading-spinner"><i class="fas fa-spinner fa-spin"></i> Chargement des logs...</div>';
        
        try {
            const result = await this.api('admin/logs?lines=100');
            
            if (result.error) {
                logsElement.innerHTML = `<div class="error-message">Erreur: ${this.escapeHtml(result.error)}</div>`;
                return;
            }

            if (!result.logs) {
                logsElement.innerHTML = '<div class="empty-state">Aucun log disponible</div>';
                return;
            }

            // Parse logs
            const logLines = result.logs.split('\n').filter(line => line.trim());
            const parsedLogs = logLines.map(line => this.parseLogLine(line));

            // Render logs
            logsElement.innerHTML = parsedLogs.map((log, index) => `
                <div class="log-entry ${log.level}" onclick="app.showLogDetails(${index})">
                    <span class="log-time">${this.escapeHtml(log.timestamp)}</span>
                    <span class="log-separator">|</span>
                    <span class="log-message">${this.escapeHtml(log.message)}</span>
                </div>
            `).join('');
            
            // Store logs for modal access
            this.currentLogs = parsedLogs;
            
            logsElement.scrollTop = logsElement.scrollHeight;
        } catch (error) {
            logsElement.innerHTML = `<div class="error-message">Erreur: ${this.escapeHtml(error.message)}</div>`;
        }
    }

    parseLogLine(line) {
        // Regex to parse journalctl output: "Dec 17 16:38:43 hostname process[pid]: message"
        // Or simpler: "Date Time Hostname Process: Message"
        // We want to extract Date+Time and Message.
        
        // Try to match standard syslog format
        // Example: Dec 17 16:38:43 homeassistant firewall[213567]: warn: Message...
        const syslogRegex = /^([A-Z][a-z]{2}\s+\d+\s+\d{2}:\d{2}:\d{2})\s+\S+\s+([^:]+):\s+(.*)$/;
        const match = line.match(syslogRegex);

        if (match) {
            const timestamp = match[1];
            const processInfo = match[2]; // e.g., firewall[213567]
            let message = match[3];
            
            // Detect level from message if present (e.g., "warn: ...", "error: ...")
            let level = 'info';
            if (message.toLowerCase().startsWith('warn:') || message.toLowerCase().startsWith('warning:')) level = 'warning';
            else if (message.toLowerCase().startsWith('err:') || message.toLowerCase().startsWith('error:')) level = 'error';
            else if (message.toLowerCase().startsWith('fail:')) level = 'error';
            else if (message.toLowerCase().startsWith('crit:')) level = 'critical';
            else if (message.toLowerCase().startsWith('dbug:') || message.toLowerCase().startsWith('debug:')) level = 'debug';

            return {
                raw: line,
                timestamp: timestamp,
                process: processInfo,
                message: message,
                level: level
            };
        }

        // Fallback for other formats
        return {
            raw: line,
            timestamp: '',
            process: '',
            message: line,
            level: 'info'
        };
    }

    showLogDetails(index) {
        const log = this.currentLogs[index];
        if (!log) return;

        document.getElementById('modal-title').textContent = 'Details du Log';
        document.getElementById('modal-body').innerHTML = `
            <div class="log-details">
                <div class="log-detail-item">
                    <span class="label">Date:</span>
                    <span class="value">${this.escapeHtml(log.timestamp || 'N/A')}</span>
                </div>
                <div class="log-detail-item">
                    <span class="label">Processus:</span>
                    <span class="value">${this.escapeHtml(log.process || 'N/A')}</span>
                </div>
                <div class="log-detail-item">
                    <span class="label">Niveau:</span>
                    <span class="value"><span class="log-badge ${log.level}">${log.level.toUpperCase()}</span></span>
                </div>
                <div class="log-detail-item full-width">
                    <span class="label">Message:</span>
                    <div class="value code-block">${this.escapeHtml(log.message)}</div>
                </div>
                <div class="log-detail-item full-width">
                    <span class="label">Ligne brute:</span>
                    <div class="value code-block small">${this.escapeHtml(log.raw)}</div>
                </div>
            </div>
        `;
        
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.copyToClipboard('${this.escapeHtml(log.raw).replace(/'/g, "\\'")}')">
                <i class="fas fa-copy"></i> Copier
            </button>
            <button class="btn btn-sm" onclick="app.closeModal()">Fermer</button>
        `;
        
        document.getElementById('modal').classList.add('active');
    }

    async copyToClipboard(text) {
        try {
            await navigator.clipboard.writeText(text);
            this.showToast({ title: 'Copie', message: 'Log copie dans le presse-papier', severity: 0 });
        } catch (err) {
            console.error('Failed to copy:', err);
            this.showToast({ title: 'Erreur', message: 'Impossible de copier', severity: 2 });
        }
    }

    setAdminResult(elementId, type, message) {
        const element = document.getElementById(elementId);
        element.className = `admin-result ${type}`;
        element.innerHTML = message;
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
