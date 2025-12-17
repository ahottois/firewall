// Network Firewall Monitor - Frontend Application

class FirewallApp {
    constructor() {
        this.currentPage = 'dashboard';
        this.eventSource = null;
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
        // Update nav
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.page === page);
        });

        // Update pages
        document.querySelectorAll('.page').forEach(p => {
            p.classList.toggle('active', p.id === `${page}-page`);
        });

        // Update title
        const titles = {
            dashboard: 'Dashboard',
            devices: 'Appareils Réseau',
            alerts: 'Alertes',
            traffic: 'Trafic Réseau',
            settings: 'Paramètres'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;
        this.loadPageData(page);
    }

    loadPageData(page) {
        switch (page) {
            case 'dashboard':
                this.loadDashboard();
                break;
            case 'devices':
                this.loadDevices();
                break;
            case 'alerts':
                this.loadAlerts();
                break;
            case 'traffic':
                this.loadTraffic();
                break;
            case 'settings':
                this.loadSettings();
                break;
        }
    }

    // Event Listeners
    setupEventListeners() {
        document.getElementById('scan-btn').addEventListener('click', () => this.scanNetwork());
        document.getElementById('mark-all-read').addEventListener('click', () => this.markAllAlertsRead());
        
        document.querySelectorAll('.filter-buttons .btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.filter-buttons .btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                this.loadDevices(e.target.dataset.filter);
            });
        });

        // Modal close
        document.querySelector('.modal-close').addEventListener('click', () => this.closeModal());
        document.getElementById('modal').addEventListener('click', (e) => {
            if (e.target.id === 'modal') this.closeModal();
        });
    }

    // API Calls
    async api(endpoint, options = {}) {
        try {
            const response = await fetch(`/api/${endpoint}`, {
                ...options,
                headers: {
                    'Content-Type': 'application/json',
                    ...options.headers
                }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return await response.json();
        } catch (error) {
            console.error(`API Error (${endpoint}):`, error);
            throw error;
        }
    }

    // Real-time Notifications
    connectNotifications() {
        if (this.eventSource) {
            this.eventSource.close();
        }

        this.eventSource = new EventSource('/api/notifications/stream');

        this.eventSource.addEventListener('alert', (e) => {
            const alert = JSON.parse(e.data);
            this.showToast(alert);
            this.updateAlertBadge();
            if (this.currentPage === 'dashboard') {
                this.loadDashboard();
            } else if (this.currentPage === 'alerts') {
                this.loadAlerts();
            }
        });

        this.eventSource.addEventListener('connected', () => {
            console.log('Connected to notification stream');
        });

        this.eventSource.onerror = () => {
            console.log('Notification stream disconnected, reconnecting...');
            setTimeout(() => this.connectNotifications(), 5000);
        };
    }

    // Dashboard
    async loadDashboard() {
        try {
            const [devices, unknownDevices, alerts, unreadCount, stats, status] = await Promise.all([
                this.api('devices'),
                this.api('devices/unknown'),
                this.api('alerts?count=10'),
                this.api('alerts/unread/count'),
                this.api('traffic/stats?hours=1'),
                this.api('system/status')
            ]);

            // Update stats
            document.getElementById('total-devices').textContent = devices.length;
            document.getElementById('online-devices').textContent = devices.filter(d => d.status === 1).length;
            document.getElementById('unknown-devices').textContent = unknownDevices.length;
            document.getElementById('unread-alerts').textContent = unreadCount;

            // Update alert badge
            this.updateAlertBadgeValue(unreadCount);

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
            container.innerHTML = '<div class="empty-state"><div class="empty-state-icon">?</div><p>Aucune alerte récente</p></div>';
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
            container.innerHTML = '<div class="empty-state"><div class="empty-state-icon">?</div><p>Tous les appareils sont connus</p></div>';
            return;
        }

        container.innerHTML = devices.map(device => `
            <div class="device-item">
                <div class="device-info">
                    <div class="device-mac">${this.escapeHtml(device.macAddress)}</div>
                    <div class="device-ip">${this.escapeHtml(device.ipAddress || 'N/A')} • ${this.escapeHtml(device.vendor || 'Unknown')}</div>
                </div>
                <button class="btn btn-sm btn-success" onclick="app.trustDevice(${device.id})">Approuver</button>
            </div>
        `).join('');
    }

    renderTrafficStats(stats) {
        const container = document.getElementById('traffic-stats');
        container.innerHTML = `
            <div class="stat-item">
                <div class="stat-label">Paquets Total</div>
                <div class="stat-value">${this.formatNumber(stats.totalPackets)}</div>
            </div>
            <div class="stat-item">
                <div class="stat-label">Données</div>
                <div class="stat-value">${this.formatBytes(stats.totalBytes)}</div>
            </div>
            <div class="stat-item">
                <div class="stat-label">Appareils Uniques</div>
                <div class="stat-value">${stats.uniqueDevices}</div>
            </div>
            <div class="stat-item">
                <div class="stat-label">Suspects</div>
                <div class="stat-value">${stats.suspiciousPackets}</div>
            </div>
        `;
    }

    // Devices
    async loadDevices(filter = 'all') {
        try {
            let devices;
            switch (filter) {
                case 'online':
                    devices = await this.api('devices/online');
                    break;
                case 'unknown':
                    devices = await this.api('devices/unknown');
                    break;
                default:
                    devices = await this.api('devices');
            }

            this.renderDevicesTable(devices);
        } catch (error) {
            console.error('Error loading devices:', error);
        }
    }

    renderDevicesTable(devices) {
        const tbody = document.getElementById('devices-table');
        if (!devices.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun appareil trouvé</td></tr>';
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
                        ${!device.isKnown ? `<button class="btn btn-sm btn-success" onclick="app.trustDevice(${device.id})">?</button>` : ''}
                        <button class="btn btn-sm" onclick="app.showDeviceDetails(${device.id})">??</button>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteDevice(${device.id})">?</button>
                    </div>
                </td>
            </tr>
        `).join('');
    }

    async trustDevice(id) {
        try {
            await this.api(`devices/${id}/trust`, {
                method: 'POST',
                body: JSON.stringify({ trusted: true })
            });
            this.showToast({ title: 'Appareil approuvé', severity: 0 });
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
        document.getElementById('modal-title').textContent = 'Détails Appareil';
        document.getElementById('modal-body').innerHTML = `
            <div class="settings-info">
                <div class="settings-item">
                    <div class="settings-label">MAC Address</div>
                    <div class="settings-value"><code>${this.escapeHtml(device.macAddress)}</code></div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">IP Address</div>
                    <div class="settings-value">${this.escapeHtml(device.ipAddress || 'N/A')}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Fabricant</div>
                    <div class="settings-value">${this.escapeHtml(device.vendor || 'Unknown')}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Première Vue</div>
                    <div class="settings-value">${this.formatDate(device.firstSeen)}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Dernière Vue</div>
                    <div class="settings-value">${this.formatDate(device.lastSeen)}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Status</div>
                    <div class="settings-value">${device.isKnown ? '? Connu' : '?? Inconnu'} ${device.isTrusted ? '• ?? Approuvé' : ''}</div>
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-sm" onclick="app.closeModal()">Fermer</button>
        `;
        document.getElementById('modal').classList.add('active');
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
            container.innerHTML = '<div class="empty-state"><div class="empty-state-icon">?</div><p>Aucune alerte</p></div>';
            return;
        }

        container.innerHTML = alerts.map(alert => `
            <div class="alert-item ${this.getSeverityClass(alert.severity)} ${alert.isRead ? '' : 'unread'}">
                <div class="alert-content">
                    <div class="alert-title">${this.getAlertTypeEmoji(alert.type)} ${this.escapeHtml(alert.title)}</div>
                    <div class="alert-message">${this.escapeHtml(alert.message)}</div>
                    <div class="alert-time">
                        ${this.formatDate(alert.timestamp)}
                        ${alert.sourceIp ? ` • Source: ${this.escapeHtml(alert.sourceIp)}` : ''}
                        ${alert.protocol ? ` • ${this.escapeHtml(alert.protocol)}` : ''}
                    </div>
                </div>
                <div class="alert-actions">
                    ${!alert.isRead ? `<button class="btn btn-sm" onclick="app.markAlertRead(${alert.id})">? Lu</button>` : ''}
                    ${!alert.isResolved ? `<button class="btn btn-sm btn-success" onclick="app.resolveAlert(${alert.id})">Résolu</button>` : ''}
                </div>
            </div>
        `).join('');
    }

    async markAlertRead(id) {
        await this.api(`alerts/${id}/read`, { method: 'POST' });
        this.loadAlerts();
        this.updateAlertBadge();
    }

    async resolveAlert(id) {
        await this.api(`alerts/${id}/resolve`, { method: 'POST' });
        this.loadAlerts();
        this.updateAlertBadge();
    }

    async markAllAlertsRead() {
        await this.api('alerts/read-all', { method: 'POST' });
        this.loadAlerts();
        this.updateAlertBadge();
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
                <div class="protocol-bar">
                    <div class="protocol-bar-fill" style="width: ${(count / total) * 100}%"></div>
                </div>
                <span>${this.formatNumber(count)}</span>
            </div>
        `).join('') || '<div class="empty-state">Aucune donnée de protocole</div>';
    }

    // Settings
    async loadSettings() {
        try {
            const [status, interfaces] = await Promise.all([
                this.api('system/status'),
                this.api('system/interfaces')
            ]);

            document.getElementById('system-info').innerHTML = `
                <div class="settings-item">
                    <div class="settings-label">Status Capture</div>
                    <div class="settings-value">${status.isCapturing ? '?? Active' : '?? Inactive'}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Interface</div>
                    <div class="settings-value">${this.escapeHtml(status.currentInterface || 'Non sélectionnée')}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Version</div>
                    <div class="settings-value">${status.version}</div>
                </div>
                <div class="settings-item">
                    <div class="settings-label">Heure Serveur</div>
                    <div class="settings-value">${this.formatDate(status.serverTime)}</div>
                </div>
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

    // Network Scan
    async scanNetwork() {
        try {
            document.getElementById('scan-btn').disabled = true;
            document.getElementById('scan-btn').innerHTML = '<span>?</span> Scan en cours...';
            
            await this.api('system/scan', { method: 'POST' });
            
            this.showToast({ title: 'Scan réseau', message: 'Scan initié avec succès', severity: 0 });
            
            setTimeout(() => {
                this.loadPageData(this.currentPage);
            }, 5000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 3 });
        } finally {
            document.getElementById('scan-btn').disabled = false;
            document.getElementById('scan-btn').innerHTML = '<span>??</span> Scanner le réseau';
        }
    }

    // Toast Notifications
    showToast(alert) {
        const container = document.getElementById('toast-container');
        const toast = document.createElement('div');
        toast.className = `toast ${this.getSeverityClass(alert.severity)}`;
        toast.innerHTML = `
            <div class="toast-title">${this.getAlertTypeEmoji(alert.type)} ${this.escapeHtml(alert.title)}</div>
            <div class="toast-message">${this.escapeHtml(alert.message || '')}</div>
        `;
        container.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 300);
        }, 5000);
    }

    // Modal
    closeModal() {
        document.getElementById('modal').classList.remove('active');
    }

    // Auto Refresh
    startAutoRefresh() {
        setInterval(() => {
            if (this.currentPage === 'dashboard') {
                this.loadDashboard();
            }
        }, 30000);
    }

    // Helpers
    getSeverityClass(severity) {
        const classes = ['info', 'low', 'medium', 'high', 'critical'];
        return classes[severity] || 'info';
    }

    getStatusClass(status) {
        const classes = ['unknown', 'online', 'offline', 'blocked'];
        return classes[status] || 'unknown';
    }

    getStatusText(status) {
        const texts = ['Inconnu', 'En ligne', 'Hors ligne', 'Bloqué'];
        return texts[status] || 'Inconnu';
    }

    getAlertTypeEmoji(type) {
        const emojis = {
            0: '??', // NewDevice
            1: '?', // UnknownDevice
            2: '??', // SuspiciousTraffic
            3: '??', // PortScan
            4: '??', // ArpSpoofing
            5: '??', // DnsAnomaly
            6: '??', // HighTrafficVolume
            7: '??', // MalformedPacket
            8: '??', // UnauthorizedAccess
            9: '??'  // ManInTheMiddle
        };
        return emojis[type] || '??';
    }

    formatDate(dateStr) {
        if (!dateStr) return 'N/A';
        const date = new Date(dateStr);
        const now = new Date();
        const diff = now - date;
        
        if (diff < 60000) return 'À l\'instant';
        if (diff < 3600000) return `Il y a ${Math.floor(diff / 60000)} min`;
        if (diff < 86400000) return `Il y a ${Math.floor(diff / 3600000)} h`;
        
        return date.toLocaleDateString('fr-FR', {
            day: '2-digit',
            month: '2-digit',
            year: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        });
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
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
}

// Initialize app
const app = new FirewallApp();
