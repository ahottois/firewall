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
        this.alertHub = null;
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
        this.dhcpConfigLevel = 'easy'; // Niveau par défaut pour DHCP
    }

    // ==========================================
    // NAVIGATION SETUP
    // ==========================================

    setupNavigation() {
        // Gestion du clic sur les éléments de navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', (e) => {
                const page = item.dataset.page;
                if (page) {
                    // Cas spécial pour la page setup - redirection vers une autre page HTML
                    if (page === 'setup') {
                        window.location.href = '/setup.html';
                        return;
                    }
                    this.navigateTo(page);
                }
            });
        });
    }

    setupEventListeners() {
        // Fermeture des modals
        document.querySelectorAll('.modal-close').forEach(btn => {
            btn.addEventListener('click', () => {
                btn.closest('.modal').classList.remove('active');
            });
        });

        // Clic en dehors du modal
        document.querySelectorAll('.modal').forEach(modal => {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    modal.classList.remove('active');
                }
            });
        });

        // Filtres des appareils
        document.querySelectorAll('.btn-filter').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.btn-filter').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.filterDevices(btn.dataset.filter);
            });
        });
    }

    setupSorting() {
        document.querySelectorAll('.sortable-table th.sortable').forEach(th => {
            th.addEventListener('click', () => {
                const table = th.closest('table');
                const column = th.dataset.sort;
                const tbody = table.querySelector('tbody');
                const rows = Array.from(tbody.querySelectorAll('tr'));

                // Toggle direction
                const currentDir = this.sortDirection[column] || 'asc';
                const newDir = currentDir === 'asc' ? 'desc' : 'asc';
                this.sortDirection[column] = newDir;

                // Update headers
                table.querySelectorAll('th.sortable').forEach(h => {
                    h.classList.remove('asc', 'desc');
                });
                th.classList.add(newDir);

                // Sort rows
                rows.sort((a, b) => {
                    const aVal = a.querySelector(`td:nth-child(${th.cellIndex + 1})`).textContent;
                    const bVal = b.querySelector(`td:nth-child(${th.cellIndex + 1})`).textContent;
                    
                    if (newDir === 'asc') {
                        return aVal.localeCompare(bVal, 'fr', { numeric: true });
                    }
                    return bVal.localeCompare(aVal, 'fr', { numeric: true });
                });

                rows.forEach(row => tbody.appendChild(row));
            });
        });
    }

    // ==========================================
    // API HELPER
    // ==========================================

    async api(endpoint, options = {}) {
        const url = `/api/${endpoint}`;
        const config = {
            headers: {
                'Content-Type': 'application/json',
                ...options.headers
            },
            ...options
        };

        const response = await fetch(url, config);
        
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || `HTTP ${response.status}`);
        }

        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
            return response.json();
        }
        return response.text();
    }

    // ==========================================
    // SIGNALR / NOTIFICATIONS
    // ==========================================

    async connectNotifications() {
        try {
            const connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/alerts')
                .withAutomaticReconnect()
                .build();

            connection.on('NewAlert', (alert) => {
                this.showToast(alert);
                this.updateAlertBadge();
            });

            await connection.start();
            console.log('Connecté au hub des alertes');
        } catch (error) {
            console.error('Erreur connexion hub alertes:', error);
        }
    }

    async connectDeviceHub() {
        try {
            this.deviceHub = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/devices')
                .withAutomaticReconnect()
                .build();

            this.deviceHub.on('DeviceUpdated', (device) => {
                if (this.currentPage === 'devices') {
                    this.loadDevices();
                }
            });

            this.deviceHub.on('NewDeviceDetected', (device) => {
                this.showToast({
                    title: 'Nouvel appareil',
                    message: `${device.macAddress} détecté sur le réseau`,
                    severity: 1
                });
                if (this.currentPage === 'devices') {
                    this.loadDevices();
                }
            });

            await this.deviceHub.start();
            console.log('Connecté au hub des appareils');
        } catch (error) {
            console.error('Erreur connexion hub appareils:', error);
        }
    }

    // ==========================================
    // UTILITY METHODS
    // ==========================================
    
    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    formatDate(dateString) {
        if (!dateString) return '-';
        const date = new Date(dateString);
        const now = new Date();
        const diff = now - date;
        
        if (diff < 60000) return 'À l\'instant';
        if (diff < 3600000) return `Il y a ${Math.floor(diff / 60000)} min`;
        if (diff < 86400000) return `Il y a ${Math.floor(diff / 3600000)} h`;
        
        return date.toLocaleDateString('fr-FR', {
            day: '2-digit',
            month: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    getStatusClass(status) {
        const statusMap = {
            0: 'unknown', 'Unknown': 'unknown',
            1: 'online', 'Online': 'online',
            2: 'offline', 'Offline': 'offline',
            3: 'blocked', 'Blocked': 'blocked'
        };
        return statusMap[status] || 'unknown';
    }

    getStatusText(status) {
        const statusMap = {
            0: 'Inconnu', 'Unknown': 'Inconnu',
            1: 'En ligne', 'Online': 'En ligne',
            2: 'Hors ligne', 'Offline': 'Hors ligne',
            3: 'Bloqué', 'Blocked': 'Bloqué'
        };
        return statusMap[status] || 'Inconnu';
    }

    // ==========================================
    // TOAST NOTIFICATIONS
    // ==========================================

    showToast(alert) {
        const container = document.getElementById('toast-container');
        if (!container) return;

        const severityClasses = ['info', 'warning', 'danger', 'critical'];
        const severityClass = severityClasses[alert.severity] || 'info';

        const toast = document.createElement('div');
        toast.className = `toast ${severityClass}`;
        toast.innerHTML = `
            <div class="toast-header">
                <strong>${this.escapeHtml(alert.title || 'Notification')}</strong>
                <button class="toast-close">&times;</button>
            </div>
            <div class="toast-body">${this.escapeHtml(alert.message)}</div>
        `;

        toast.querySelector('.toast-close').addEventListener('click', () => {
            toast.remove();
        });

        container.appendChild(toast);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            if (toast.parentElement) toast.remove();
        }, 5000);
    }

    // ==========================================
    // DASHBOARD
    // ==========================================

    async loadDashboard() {
        try {
            // Load security dashboard data
            const [security, devices, alerts] = await Promise.all([
                this.api('security/dashboard').catch(() => ({})),
                this.api('devices').catch(() => []),
                this.api('alerts?count=5').catch(() => []),
                this.api('settings/system').catch(() => ({}))
            ]);

            // Update traffic overview
            this.updateTrafficCharts(security);
            this.updateTopThreats(security.recentThreats || []);
            this.updateFirewallRules();
            this.updateSystemStatus(security);
            this.updateAdminStatus(security);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    updateTrafficCharts(security) {
        const stats = security.threatStats || {};
        const allowed = stats.allowedCount || 0;
        const blocked = stats.blockedCount || 0;
        const total = allowed + blocked || 1;

        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;

        // Update donut charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.setProperty('--percent', allowedPercent);
        }
        if (blockedChart) {
            blockedChart.style.setProperty('--percent', blockedPercent);
        }
    }

    updateTopThreats(threats) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        if (!threats.length) {
            container.innerHTML = '<p class="empty-state">Aucune menace détectée</p>';
            return;
        }

        container.innerHTML = threats.slice(0, 5).map(threat => `
            <div class="threat-item">
                <span class="threat-name">${this.escapeHtml(threat.sourceIp || 'Inconnu')}</span>
                <span class="threat-type">${this.escapeHtml(threat.threatType || 'N/A')}</span>
                <span class="threat-count">${threat.count || 1}</span>
            </div>
        `).join('');
    }

    updateFirewallRules() {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        // Example static rules - can be made dynamic
        tbody.innerHTML = `
            <tr>
                <td>R001</td>
                <td><span class="status-badge online">Allow</span></td>
                <td>192.168.1.0/24</td>
                <td>Any</td>
                <td>All</td>
                <td><span class="status-badge online">Active</span></td>
            </tr>
            <tr>
                <td>R002</td>
                <td><span class="status-badge blocked">Block</span></td>
                <td>External</td>
                <td>22/TCP</td>
                <td>SSH</td>
                <td><span class="status-badge online">Active</span></td>
            </tr>
        `;
    }

    updateSystemStatus(security) {
        const score = security.securityScore || {};
        const cpuText = document.getElementById('cpu-status-text');
        if (cpuText) {
            cpuText.textContent = `Score de sécurité: ${score.overallScore || 0}/100`;
        }
    }

    updateAdminStatus(security) {
        const status = document.getElementById('service-status');
        if (status) {
            status.textContent = security.adminStatus === 'running' ? 'En cours' : 'Arrêté';
            status.className = `status-badge ${security.adminStatus === 'running' ? 'online' : 'offline'}`;
        }
    }

    startAutoRefresh() {
        // Auto-refresh dashboard every 30 seconds
        setInterval(() => {
            if (this.currentPage === 'dashboard') {
                this.loadDashboard();
            }
        }, 30000);
    }

    updateAlertBadge() {
        const badge = document.getElementById('alert-badge');
        if (badge) {
            this.api('alerts/unread/count').then(data => {
                const count = data.count || 0;
                badge.textContent = count;
                badge.style.display = count > 0 ? 'inline-flex' : 'none';
            }).catch(() => {});
        }
    }

    // ==========================================
    // ALERTS & LOGS
    // ==========================================

    async loadAlerts() {
        try {
            const [alerts, stats] = await Promise.all([
                this.api('alerts?count=50'),
                this.api('alerts/stats').catch(() => ({}))
            ]);

            // Update stats
            document.getElementById('total-alerts-count').textContent = stats.total || 0;
            document.getElementById('critical-alerts-count').textContent = stats.critical || 0;
            document.getElementById('blocked-attempts-count').textContent = stats.blocked || 0;
            document.getElementById('last-24h-count').textContent = stats.last24h || 0;

            // Render alerts table
            this.renderAlertsTable(alerts);
            
            // Load security logs
            this.loadSecurityLogs();

        } catch (error) {
            console.error('Error loading alerts:', error);
        }
    }

    renderAlertsTable(alerts) {
        const tbody = document.getElementById('alerts-table');
        if (!tbody) return;

        if (!alerts.length) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucune alerte</td></tr>';
            return;
        }

        tbody.innerHTML = alerts.map(alert => `
            <tr class="${alert.isRead ? '' : 'unread'}">
                <td><span class="severity-badge severity-${alert.severity}">${this.getSeverityText(alert.severity)}</span></td>
                <td>${this.escapeHtml(alert.type)}</td>
                <td>${this.escapeHtml(alert.message)}</td>
                <td>${this.escapeHtml(alert.sourceIp || '-')}</td>
                <td>${this.formatDate(alert.timestamp)}</td>
                <td>
                    <button class="btn btn-sm" onclick="app.markAlertRead(${alert.id})" title="Marquer comme lu">
                        <i class="fas fa-check"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    getSeverityText(severity) {
        const severities = ['Info', 'Moyen', 'Haut', 'Critique'];
        return severities[severity] || 'Info';
    }

    async loadSecurityLogs() {
        try {
            const logs = await this.api('logs/security?count=50');
            const tbody = document.getElementById('security-logs-table');
            if (!tbody) return;

            if (!logs.length) {
                tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Aucun log de sécurité</td></tr>';
                return;
            }

            tbody.innerHTML = logs.map(log => `
                <tr>
                    <td>${this.formatDate(log.timestamp)}</td>
                    <td><span class="severity-badge severity-${log.severity}">${log.severity}</span></td>
                    <td>${this.escapeHtml(log.category)}</td>
                    <td>${this.escapeHtml(log.actionTaken)}</td>
                    <td>${this.escapeHtml(log.message)}</td>
                    <td>${this.escapeHtml(log.sourceIp || '-')}</td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading security logs:', error);
        }
    }

    filterLogs() {
        // Implement log filtering
        this.loadSecurityLogs();
    }

    async markAlertRead(id) {
        try {
            await this.api(`alerts/${id}/read`, { method: 'POST' });
            this.loadAlerts();
        } catch (error) {
            console.error('Error marking alert as read:', error);
        }
    }

    // ==========================================
    // AGENTS
    // ==========================================

    async loadAgents() {
        try {
            const agents = await this.api('agents');
            this.currentAgents = agents;

            const grid = document.getElementById('agents-grid');
            const empty = document.getElementById('agents-empty');

            if (!agents.length) {
                grid.innerHTML = '';
                empty.style.display = 'block';
                return;
            }

            empty.style.display = 'none';
            grid.innerHTML = agents.map(agent => this.createAgentCard(agent)).join('');
        } catch (error) {
            console.error('Error loading agents:', error);
        }
    }

    createAgentCard(agent) {
        const isOnline = agent.status === 1 || agent.status === 'Online';
        const statusClass = isOnline ? 'online' : 'offline';
        const statusText = isOnline ? 'En ligne' : 'Hors ligne';
        
        // Icône OS
        let osIcon = 'fa-server';
        const osLower = (agent.os || '').toLowerCase();
        if (osLower.includes('linux') || osLower.includes('ubuntu') || osLower.includes('debian')) {
            osIcon = 'fab fa-linux';
        } else if (osLower.includes('windows')) {
            osIcon = 'fab fa-windows';
        } else if (osLower.includes('mac') || osLower.includes('darwin')) {
            osIcon = 'fab fa-apple';
        }
        
        // Barres de progression pour les métriques
        const cpuPercent = agent.cpuUsage || 0;
        const memPercent = agent.memoryUsage || 0;
        const diskPercent = agent.diskUsage || 0;
        
        // Couleurs des métriques selon le niveau
        const getCpuColor = (val) => val > 80 ? 'var(--danger)' : val > 50 ? 'var(--warning)' : 'var(--accent-secondary)';
        const getMemColor = (val) => val > 85 ? 'var(--danger)' : val > 70 ? 'var(--warning)' : 'var(--success)';
        
        return `
            <div class="agent-card ${statusClass}">
                <div class="card-header">
                    <h4>${this.escapeHtml(agent.hostname)}</h4>
                    <span class="status-badge ${statusClass}">${statusText}</span>
                </div>
                <div class="card-body">
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-network-wired"></i>
                            IP
                        </span>
                        <span class="agent-metric-value">${this.escapeHtml(agent.ipAddress || '-')}</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="${osIcon}"></i>
                            OS
                        </span>
                        <span class="agent-metric-value" style="font-size: 0.8rem;">${this.escapeHtml(agent.os || 'Inconnu')}</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-microchip"></i>
                            CPU
                        </span>
                        <div class="agent-metric-bar">
                            <div class="agent-metric-bar-fill cpu" style="width: ${cpuPercent}%; background: ${getCpuColor(cpuPercent)};"></div>
                        </div>
                        <span class="agent-metric-value">${cpuPercent.toFixed(1)}%</span>
                    </div>
                    <div class="agent-metric">
                        <span class="agent-metric-label">
                            <i class="fas fa-memory"></i>
                            RAM
                        </span>
                        <div class="agent-metric-bar">
                            <div class="agent-metric-bar-fill memory" style="width: ${memPercent}%; background: ${getMemColor(memPercent)};"></div>
                        </div>
                        <span class="agent-metric-value">${memPercent.toFixed(1)}%</span>
                    </div>
                </div>
                <div class="card-footer">
                    <button class="btn btn-sm btn-primary" onclick="app.showAgentDetails(${agent.id})">
                        <i class="fas fa-info-circle"></i> Détails
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="app.deleteAgent(${agent.id})">
                        <i class="fas fa-trash"></i> Supprimer
                    </button>
                </div>
            </div>
        `;
    }

    async showAgentDetails(agentId) {
        try {
            const agent = this.currentAgents.find(a => a.id === agentId);
            if (!agent) {
                this.showToast({ title: 'Erreur', message: 'Agent non trouvé', severity: 2 });
                return;
            }

            // Remplir les informations de base
            document.getElementById('agent-detail-hostname').textContent = agent.hostname || 'Inconnu';
            document.getElementById('agent-detail-os').textContent = agent.os || 'Inconnu';
            document.getElementById('agent-detail-ip').textContent = agent.ipAddress || 'Inconnue';
            document.getElementById('agent-detail-seen').textContent = this.formatDate(agent.lastSeen);

            // Initialiser les sections
            let hardwareHtml = '';
            let networkHtml = '';
            let systemHtml = '';

            // Tenter de parser le JSON des détails
            let details = null;
            if (agent.detailsJson) {
                try {
                    // Le detailsJson peut être échappé, donc on décode
                    let jsonStr = agent.detailsJson;
                    if (jsonStr.startsWith('"') && jsonStr.endsWith('"')) {
                        jsonStr = JSON.parse(jsonStr);
                    }
                    details = typeof jsonStr === 'string' ? JSON.parse(jsonStr) : jsonStr;
                } catch (e) {
                    console.warn('Impossible de parser detailsJson:', e, agent.detailsJson);
                }
            }

            if (details) {
                // ========== HARDWARE ==========
                if (details.hardware) {
                    const hw = details.hardware;
                    hardwareHtml = `
                        <div class="detail-row"><strong>Processeur:</strong> ${this.escapeHtml(hw.cpuModel || 'Inconnu')}</div>
                        <div class="detail-row"><strong>Coeurs:</strong> ${hw.cpuCores || '-'}</div>
                        ${hw.cpuFreqMhz ? `<div class="detail-row"><strong>Fréquence:</strong> ${hw.cpuFreqMhz} MHz</div>` : ''}
                        ${hw.cpuTemp && hw.cpuTemp !== 'N/A' ? `<div class="detail-row"><strong>Température:</strong> ${hw.cpuTemp}°C</div>` : ''}
                        <div class="detail-row"><strong>RAM:</strong> ${this.escapeHtml(hw.usedRam || '-')} / ${this.escapeHtml(hw.totalRam || '-')}</div>
                        ${hw.totalSwap && hw.totalSwap !== '0 GB' ? `<div class="detail-row"><strong>Swap:</strong> ${this.escapeHtml(hw.usedSwap || '0')} / ${this.escapeHtml(hw.totalSwap)}</div>` : ''}
                        <div class="detail-row"><strong>Disque /:</strong> ${this.escapeHtml(hw.diskUsed || '-')} / ${this.escapeHtml(hw.diskTotal || '-')}</div>
                    `;
                }

                // ========== SYSTEM ==========
                if (details.system) {
                    const sys = details.system;
                    systemHtml = `
                        <div class="detail-row"><strong>Kernel:</strong> ${this.escapeHtml(sys.kernel || '-')}</div>
                        <div class="detail-row"><strong>Architecture:</strong> ${this.escapeHtml(sys.arch || '-')}</div>
                        <div class="detail-row"><strong>Uptime:</strong> ${this.escapeHtml(sys.uptime || '-')}</div>
                        <div class="detail-row"><strong>Dernier boot:</strong> ${this.escapeHtml(sys.lastBoot || '-')}</div>
                        <div class="detail-row"><strong>Load Average:</strong> ${sys.loadAvg1 || 0} / ${sys.loadAvg5 || 0} / ${sys.loadAvg15 || 0}</div>
                        <div class="detail-row"><strong>Processus:</strong> ${sys.processCount || '-'}</div>
                    `;
                }

                // ========== NETWORK ==========
                if (details.network) {
                    const net = details.network;
                    networkHtml = `
                        <div class="detail-row"><strong>Interface:</strong> ${this.escapeHtml(net.primaryInterface || '-')}</div>
                        <div class="detail-row"><strong>IP:</strong> ${this.escapeHtml(net.primaryIp || agent.ipAddress || '-')}</div>
                        <div class="detail-row"><strong>MAC:</strong> ${this.escapeHtml(net.primaryMac || agent.macAddress || '-')}</div>
                        <div class="detail-row"><strong>Trafic RX:</strong> ${net.rxTotalGb || 0} GB</div>
                        <div class="detail-row"><strong>Trafic TX:</strong> ${net.txTotalGb || 0} GB</div>
                    `;
                    
                    // Interfaces supplémentaires
                    if (net.interfaces && Array.isArray(net.interfaces) && net.interfaces.length > 1) {
                        networkHtml += '<div class="detail-row" style="margin-top: 10px;"><strong>Autres interfaces:</strong></div>';
                        net.interfaces.forEach(iface => {
                            if (iface.name !== net.primaryInterface) {
                                const ips = iface.ips ? iface.ips.join(', ') : '-';
                                networkHtml += `<div class="detail-row" style="font-size: 0.85rem; margin-left: 10px;">• ${this.escapeHtml(iface.name)}: ${this.escapeHtml(ips)}</div>`;
                            }
                        });
                    }
                }

                // ========== SERVICES ==========
                if (details.services) {
                    const svc = details.services;
                    systemHtml += '<div class="detail-row" style="margin-top: 15px; border-top: 1px solid var(--border-color); padding-top: 10px;"><strong>Services:</strong></div>';
                    
                    const getStatusBadge = (status) => {
                        if (status === 'active') return '<span class="status-badge online">Actif</span>';
                        if (status === 'inactive') return '<span class="status-badge offline">Inactif</span>';
                        if (status === 'not installed') return '<span class="status-badge unknown">Non installé</span>';
                        return `<span class="status-badge unknown">${status}</span>`;
                    };
                    
                    systemHtml += `<div class="detail-row" style="margin-left: 10px;">SSH: ${getStatusBadge(svc.ssh)}</div>`;
                    systemHtml += `<div class="detail-row" style="margin-left: 10px;">Docker: ${getStatusBadge(svc.docker)}`;
                    if (svc.docker === 'active' && svc.dockerContainers > 0) {
                        systemHtml += ` (${svc.dockerContainers} conteneur${svc.dockerContainers > 1 ? 's' : ''})`;
                    }
                    systemHtml += '</div>';
                }

                // ========== DISKS ==========
                if (details.disks && Array.isArray(details.disks) && details.disks.length > 0) {
                    hardwareHtml += '<div class="detail-row" style="margin-top: 15px; border-top: 1px solid var(--border-color); padding-top: 10px;"><strong>Disques:</strong></div>';
                    details.disks.forEach(disk => {
                        const usageColor = disk.percent > 90 ? 'var(--danger)' : disk.percent > 70 ? 'var(--warning)' : 'var(--success)';
                        hardwareHtml += `
                            <div class="detail-row" style="margin-left: 10px;">
                                <span>${this.escapeHtml(disk.mount)}</span>
                                <div style="display: inline-block; width: 100px; height: 8px; background: var(--bg-primary); border-radius: 4px; margin: 0 10px; vertical-align: middle;">
                                    <div style="width: ${disk.percent}%; height: 100%; background: ${usageColor}; border-radius: 4px;"></div>
                                </div>
                                <span>${disk.used}G / ${disk.total}G (${disk.percent}%)</span>
                            </div>
                        `;
                    });
                }
            } else {
                // Fallback: afficher les métriques de base
                hardwareHtml = `
                    <div class="detail-row"><strong>CPU:</strong> ${agent.cpuUsage?.toFixed(1) || 0}%</div>
                    <div class="detail-row"><strong>Mémoire:</strong> ${agent.memoryUsage?.toFixed(1) || 0}%</div>
                    <div class="detail-row"><strong>Disque:</strong> ${agent.diskUsage?.toFixed(1) || 0}%</div>
                `;
                
                networkHtml = `
                    <div class="detail-row"><strong>IP:</strong> ${this.escapeHtml(agent.ipAddress || '-')}</div>
                    <div class="detail-row"><strong>MAC:</strong> ${this.escapeHtml(agent.macAddress || '-')}</div>
                `;
                
                systemHtml = `
                    <div class="detail-row"><strong>OS:</strong> ${this.escapeHtml(agent.os || '-')}</div>
                    <div class="detail-row"><strong>Version Agent:</strong> ${this.escapeHtml(agent.version || '1.0.0')}</div>
                    <div class="detail-row"><strong>Enregistré le:</strong> ${this.formatDate(agent.registeredAt)}</div>
                `;
            }

            // Mettre à jour le DOM
            document.getElementById('agent-hardware-info').innerHTML = hardwareHtml || '<p>Aucune information</p>';
            document.getElementById('agent-network-info').innerHTML = networkHtml || '<p>Aucune information</p>';
            document.getElementById('agent-system-info').innerHTML = systemHtml || '<p>Aucune information</p>';

            // Afficher le modal
            document.getElementById('agent-details-modal').classList.add('active');
        } catch (error) {
            console.error('Error showing agent details:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible d\'afficher les détails', severity: 2 });
        }
    }

    showInstallAgentModal() {
        document.getElementById('install-agent-modal').classList.add('active');
    }

    async getInstallScript(os) {
        try {
            const result = await this.api(`agents/install-script?os=${os}`);
            const container = document.getElementById('install-script-container');
            const command = document.getElementById('install-command');
            
            command.textContent = result.command;
            container.style.display = 'block';
        } catch (error) {
            console.error('Erreur génération script:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de générer le script', severity: 2 });
        }
    }

    copyInstallCommand() {
        const command = document.getElementById('install-command').textContent;
        navigator.clipboard.writeText(command).then(() => {
            this.showToast({ title: 'Copié', message: 'Commande copiée dans le presse-papiers', severity: 0 });
        });
    }

    async deleteAgent(id) {
        if (!confirm('Supprimer cet agent ?')) return;
        
        try {
            await this.api(`agents/${id}`, { method: 'DELETE' });
            this.loadAgents();
            this.showToast({ title: 'Succès', message: 'Agent supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de supprimer l\'agent', severity: 2 });
        }
    }

    // ==========================================
    // DEVICE METHODS
    // ==========================================

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
            document.getElementById('device-first-seen').textContent = this.formatDate(device.firstSeen);
            document.getElementById('device-last-seen').textContent = this.formatDate(device.lastSeen);

            document.getElementById('device-details-modal').classList.add('active');
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de charger les détails', severity: 2 });
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
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil mis à jour', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de sauvegarder', severity: 2 });
        }
    }

    async approveDevice(id) {
        try {
            // Utiliser l'endpoint known pour approuver (marquer comme connu et de confiance)
            await this.api(`devices/${id}/known`, { 
                method: 'POST',
                body: JSON.stringify({ known: true, description: null })
            });
            // Aussi marquer comme trusted
            await this.api(`devices/${id}/trust`, { 
                method: 'POST',
                body: JSON.stringify({ trusted: true })
            });
            this.loadDevices();
            this.showToast({ title: 'Succes', message: 'Appareil approuve', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible d\'approuver: ' + error.message, severity: 2 });
        }
    }

    async deleteDevice(id) {
        if (!confirm('Supprimer cet appareil ?')) return;

        try {
            await this.api(`devices/${id}`, { method: 'DELETE' });
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil supprimé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: 'Impossible de supprimer', severity: 2 });
        }
    }

    // ==========================================
    // OTHER PAGE LOADERS
    // ==========================================

    async loadCameras() {
        // Les caméras ne sont pas implémentées côté backend pour le moment
        const grid = document.getElementById('cameras-grid');
        if (grid) {
            grid.innerHTML = '<div class="empty-state"><i class="fas fa-video empty-state-icon"></i><p>Fonctionnalité en développement</p></div>';
        }
    }

    async loadTraffic() {
        try {
            const stats = await this.api('traffic/stats').catch(() => ({}));
            
            document.getElementById('total-packets').textContent = stats.totalPackets || 0;
            document.getElementById('inbound-packets').textContent = stats.inboundPackets || 0;
            document.getElementById('outbound-packets').textContent = stats.outboundPackets || 0;
            document.getElementById('suspicious-packets').textContent = stats.suspiciousPackets || 0;
            
            // Afficher les protocoles
            const protocolsChart = document.getElementById('protocols-chart');
            if (protocolsChart && stats.protocols) {
                protocolsChart.innerHTML = Object.entries(stats.protocols).map(([proto, count]) => `
                    <div class="protocol-item">
                        <span class="protocol-name">${this.escapeHtml(proto)}</span>
                        <span class="protocol-count">${count}</span>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading traffic:', error);
        }
    }

    async loadDhcp() {
        try {
            // Charger la config avec l'aide du niveau actuel
            const [configResponse, leases, status] = await Promise.all([
                this.api(`dhcp/config/${this.dhcpConfigLevel}`),
                this.api('dhcp/leases').catch(() => []),
                this.api('dhcp/status').catch(() => ({}))
            ]);

            this.dhcpHelp = configResponse.help || [];
            const config = configResponse.config || {};

            // Mettre à jour les boutons de niveau
            document.querySelectorAll('.dhcp-level-btn').forEach(btn => {
                btn.classList.toggle('active', btn.dataset.level === this.dhcpConfigLevel);
            });

            // Afficher le statut
            this.updateDhcpStatus(status);

            // Remplir le formulaire selon le niveau
            this.renderDhcpForm(config);
            
            // Afficher les baux
            this.renderDhcpLeases(leases);

        } catch (error) {
            console.error('Error loading DHCP:', error);
            this.showToast({ title: 'Erreur', message: 'Impossible de charger la configuration DHCP', severity: 2 });
        }
    }

    setDhcpLevel(level) {
        this.dhcpConfigLevel = level;
        this.loadDhcp();
    }

    updateDhcpStatus(status) {
        const statusEl = document.getElementById('dhcp-server-status');
        if (statusEl) {
            const isRunning = status.isRunning;
            statusEl.innerHTML = `
                <span class="status-badge ${isRunning ? 'online' : 'offline'}">
                    ${isRunning ? 'En cours' : 'Arrêté'}
                </span>
                ${status.serverIp ? `<small style="margin-left: 10px;">IP: ${status.serverIp}</small>` : ''}
                ${status.activeLeases !== undefined ? `<small style="margin-left: 10px;">Baux: ${status.activeLeases}/${status.totalIps || '?'}</small>` : ''}
            `;
        }
    }

    renderDhcpForm(config) {
        const form = document.getElementById('dhcp-config-form');
        if (!form) return;

        const getHelp = (name) => this.dhcpHelp.find(h => h.name === name);

        let html = '';

        // Switch pour activer/désactiver
        const enabledHelp = getHelp('enabled');
        html += this.createDhcpField('enabled', 'Activer le serveur DHCP', config.enabled, 'checkbox', enabledHelp);

        // === NIVEAU FACILE ===
        html += '<h4 class="dhcp-section-title"><i class="fas fa-play-circle"></i> Configuration de base</h4>';
        
        html += '<div class="form-row">';
        html += this.createDhcpField('rangeStart', 'IP de début', config.rangeStart, 'text', getHelp('rangeStart'));
        html += this.createDhcpField('rangeEnd', 'IP de fin', config.rangeEnd, 'text', getHelp('rangeEnd'));
        html += '</div>';

        html += this.createDhcpField('gateway', 'Passerelle (routeur)', config.gateway, 'text', getHelp('gateway'));

        html += '<div class="form-row">';
        html += this.createDhcpField('dns1', 'DNS Principal', config.dns1, 'text', getHelp('dns1'));
        html += this.createDhcpField('dns2', 'DNS Secondaire', config.dns2, 'text', getHelp('dns2'));
        html += '</div>';

        // === NIVEAU INTERMÉDIAIRE ===
        if (this.dhcpConfigLevel !== 'easy') {
            html += '<h4 class="dhcp-section-title"><i class="fas fa-sliders-h"></i> Options réseau</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('subnetMask', 'Masque de sous-réseau', config.subnetMask, 'text', getHelp('subnetMask'));
            html += this.createDhcpField('domainName', 'Nom de domaine', config.domainName, 'text', getHelp('domainName'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('leaseTimeMinutes', 'Durée du bail (min)', config.leaseTimeMinutes, 'number', getHelp('leaseTimeMinutes'));
            html += this.createDhcpField('networkInterface', 'Interface réseau', config.networkInterface, 'text', getHelp('networkInterface'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('ntpServer1', 'Serveur NTP', config.ntpServer1, 'text', getHelp('ntpServer1'));
            html += this.createDhcpField('authoritativeMode', 'Mode autoritaire', config.authoritativeMode, 'checkbox', getHelp('authoritativeMode'));
            html += '</div>';
        }

        // === NIVEAU EXPERT ===
        if (this.dhcpConfigLevel === 'expert') {
            html += '<h4 class="dhcp-section-title"><i class="fas fa-cogs"></i> Options avancées</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('renewalTimeMinutes', 'Temps T1 (min)', config.renewalTimeMinutes, 'number', getHelp('renewalTimeMinutes'));
            html += this.createDhcpField('rebindingTimeMinutes', 'Temps T2 (min)', config.rebindingTimeMinutes, 'number', getHelp('rebindingTimeMinutes'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('minLeaseTimeMinutes', 'Bail minimum (min)', config.minLeaseTimeMinutes, 'number', getHelp('minLeaseTimeMinutes'));
            html += this.createDhcpField('maxLeaseTimeMinutes', 'Bail maximum (min)', config.maxLeaseTimeMinutes, 'number', getHelp('maxLeaseTimeMinutes'));
            html += '</div>';

            html += '<h4 class="dhcp-section-title"><i class="fas fa-server"></i> Boot réseau (PXE)</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('nextServerIp', 'Serveur PXE', config.nextServerIp, 'text', getHelp('nextServerIp'));
            html += this.createDhcpField('bootFileName', 'Fichier de boot', config.bootFileName, 'text', getHelp('bootFileName'));
            html += '</div>';
            
            html += this.createDhcpField('tftpServerName', 'Serveur TFTP', config.tftpServerName, 'text', getHelp('tftpServerName'));

            html += '<h4 class="dhcp-section-title"><i class="fas fa-shield-alt"></i> Sécurité</h4>';
            
            html += '<div class="form-row">';
            html += this.createDhcpField('allowUnknownClients', 'Autoriser clients inconnus', config.allowUnknownClients, 'checkbox', getHelp('allowUnknownClients'));
            html += this.createDhcpField('conflictDetection', 'Détection de conflit', config.conflictDetection, 'checkbox', getHelp('conflictDetection'));
            html += '</div>';

            html += '<div class="form-row">';
            html += this.createDhcpField('conflictDetectionAttempts', 'Tentatives de détection', config.conflictDetectionAttempts, 'number', getHelp('conflictDetectionAttempts'));
            html += this.createDhcpField('offerDelayMs', 'Délai offre (ms)', config.offerDelayMs, 'number', getHelp('offerDelayMs'));
            html += '</div>';

            html += this.createDhcpField('logAllPackets', 'Journaliser tous les paquets', config.logAllPackets, 'checkbox', getHelp('logAllPackets'));
        }

        form.innerHTML = html;
    }

    createDhcpField(name, label, value, type, help) {
        const helpHtml = help ? `
            <div class="dhcp-help" title="${this.escapeHtml(help.description)}">
                <i class="fas fa-question-circle"></i>
                <div class="dhcp-help-tooltip">
                    <strong>${this.escapeHtml(help.description)}</strong>
                    ${help.example ? `<br><em>Exemple: ${this.escapeHtml(help.example)}</em>` : ''}
                    ${help.warning ? `<br><span class="text-warning">${this.escapeHtml(help.warning)}</span>` : ''}
                </div>
            </div>
        ` : '';

        if (type === 'checkbox') {
            return `
                <div class="form-group dhcp-field">
                    <label class="checkbox-label">
                        <input type="checkbox" id="dhcp-${name}" name="${name}" ${value ? 'checked' : ''}>
                        ${this.escapeHtml(label)}
                        ${helpHtml}
                    </label>
                </div>
            `;
        }

        return `
            <div class="form-group dhcp-field">
                <label for="dhcp-${name}">
                    ${this.escapeHtml(label)}
                    ${helpHtml}
                </label>
                <input type="${type}" id="dhcp-${name}" name="${name}" value="${this.escapeHtml(value || '')}" class="form-control">
            </div>
        `;
    }

    renderDhcpLeases(leases) {
        const tbody = document.getElementById('dhcp-leases-table');
        if (!tbody) return;

        if (!leases || !leases.length) {
            tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucun bail actif</td></tr>';
            return;
        }

        tbody.innerHTML = leases.map(lease => `
            <tr>
                <td>${this.escapeHtml(lease.ipAddress)}</td>
                <td class="device-mac">${this.escapeHtml(lease.macAddress)}</td>
                <td>${this.escapeHtml(lease.hostname || '-')}</td>
                <td>${this.formatDate(lease.expiration)}</td>
                <td>
                    <button class="btn btn-sm btn-danger" onclick="app.releaseDhcpLease('${this.escapeHtml(lease.macAddress)}')" title="Libérer">
                        <i class="fas fa-times"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    async saveDhcpConfig() {
        const form = document.getElementById('dhcp-config-form');
        if (!form) return;

        // Collecter les données du formulaire
        const getValue = (name) => {
            const el = document.getElementById(`dhcp-${name}`);
            if (!el) return undefined;
            if (el.type === 'checkbox') return el.checked;
            if (el.type === 'number') return parseInt(el.value) || 0;
            return el.value;
        };

        let endpoint = 'dhcp/config/easy';
        let config = {
            enabled: getValue('enabled'),
            rangeStart: getValue('rangeStart'),
            rangeEnd: getValue('rangeEnd'),
            gateway: getValue('gateway'),
            dns1: getValue('dns1'),
            dns2: getValue('dns2')
        };

        if (this.dhcpConfigLevel === 'intermediate' || this.dhcpConfigLevel === 'expert') {
            endpoint = 'dhcp/config/intermediate';
            config = {
                ...config,
                subnetMask: getValue('subnetMask'),
                leaseTimeMinutes: getValue('leaseTimeMinutes'),
                domainName: getValue('domainName'),
                networkInterface: getValue('networkInterface'),
                ntpServer1: getValue('ntpServer1'),
                authoritativeMode: getValue('authoritativeMode'),
                staticReservations: [] // À implémenter séparément
            };
        }

        if (this.dhcpConfigLevel === 'expert') {
            endpoint = 'dhcp/config/expert';
            config = {
                ...config,
                renewalTimeMinutes: getValue('renewalTimeMinutes'),
                rebindingTimeMinutes: getValue('rebindingTimeMinutes'),
                minLeaseTimeMinutes: getValue('minLeaseTimeMinutes'),
                maxLeaseTimeMinutes: getValue('maxLeaseTimeMinutes'),
                nextServerIp: getValue('nextServerIp'),
                bootFileName: getValue('bootFileName'),
                tftpServerName: getValue('tftpServerName'),
                allowUnknownClients: getValue('allowUnknownClients'),
                conflictDetection: getValue('conflictDetection'),
                conflictDetectionAttempts: getValue('conflictDetectionAttempts'),
                offerDelayMs: getValue('offerDelayMs'),
                logAllPackets: getValue('logAllPackets')
            };
        }

        try {
            await this.api(endpoint, {
                method: 'POST',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration DHCP enregistrée', severity: 0 });
            this.loadDhcp();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message || 'Impossible de sauvegarder', severity: 2 });
        }
    }

    async releaseDhcpLease(macAddress) {
        if (!confirm(`Libérer le bail pour ${macAddress} ?`)) return;

        try {
            await this.api(`dhcp/leases/${encodeURIComponent(macAddress)}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Bail libéré', severity: 0 });
            this.loadDhcp();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // PI-HOLE ADDITIONAL METHODS
    // ==========================================

    async enablePihole() {
        try {
            await this.api('pihole/enable', { method: 'POST' });
            this.showToast({ title: 'Pi-hole', message: 'Blocage activé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showDisablePiholeModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-pause"></i> Désactiver Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <p>Choisissez la durée de désactivation :</p>
            <div class="form-group">
                <select id="pihole-disable-duration" class="form-control">
                    <option value="60">1 minute</option>
                    <option value="300">5 minutes</option>
                    <option value="600">10 minutes</option>
                    <option value="1800">30 minutes</option>
                    <option value="3600">1 heure</option>
                    <option value="0">Indéfiniment</option>
                </select>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-warning" onclick="app.disablePihole()">
                <i class="fas fa-pause"></i> Désactiver
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async disablePihole() {
        const duration = document.getElementById('pihole-disable-duration').value;
        
        try {
            await this.api(`pihole/disable?seconds=${duration}`, { method: 'POST' });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Pi-hole', message: 'Blocage désactivé', severity: 1 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showPiholePasswordModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-key"></i> Changer le mot de passe Pi-hole';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nouveau mot de passe</label>
                <input type="password" id="pihole-new-password" class="form-control" placeholder="Nouveau mot de passe">
            </div>
            <div class="form-group">
                <label>Confirmer</label>
                <input type="password" id="pihole-confirm-password" class="form-control" placeholder="Confirmer le mot de passe">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.changePiholePassword()">
                <i class="fas fa-save"></i> Changer
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async changePiholePassword() {
        const password = document.getElementById('pihole-new-password').value;
        const confirm = document.getElementById('pihole-confirm-password').value;
        
        if (password !== confirm) {
            this.showToast({ title: 'Erreur', message: 'Les mots de passe ne correspondent pas', severity: 2 });
            return;
        }
        
        try {
            await this.api('pihole/password', {
                method: 'POST',
                body: JSON.stringify({ password })
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Mot de passe changé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async uninstallPihole() {
        if (!confirm('Désinstaller Pi-hole ? Cette action est irréversible.')) return;
        
        try {
            await this.api('pihole/uninstall', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Pi-hole désinstallé', severity: 0 });
            this.loadPihole();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    // ==========================================
    // WIFI MULTI-BANDS
    // ==========================================

    async loadWiFi() {
        try {
            const [config, stats] = await Promise.all([
                this.api('wifi/config').catch(() => ({})),
                this.api('wifi/stats').catch(() => ({}))
            ]);

            // Mettre à jour les stats
            const statusCard = document.getElementById('wifi-status-card');
            const statusText = document.getElementById('wifi-status-text');
            if (statusText) statusText.textContent = config.enabled ? 'Actif' : 'Inactif';
            if (statusCard) statusCard.className = `stat-card ${config.enabled ? 'success' : 'danger'}`;
            
            const totalClients = document.getElementById('wifi-clients-total');
            if (totalClients) totalClients.textContent = stats.totalClients || 0;

            const meshNodes = document.getElementById('wifi-mesh-nodes');
            if (meshNodes) meshNodes.textContent = stats.meshNodes || 0;

            // Config globale
            const wifiEnabled = document.getElementById('wifi-enabled');
            if (wifiEnabled) wifiEnabled.checked = config.enabled;

            const ssidInput = document.getElementById('wifi-ssid');
            if (ssidInput) ssidInput.value = config.globalSSID || '';

            const securitySelect = document.getElementById('wifi-security');
            if (securitySelect) securitySelect.value = config.globalSecurity || 4;

            const smartConnect = document.getElementById('wifi-smart-connect');
            if (smartConnect) smartConnect.checked = config.smartConnect;

            const fastRoaming = document.getElementById('wifi-fast-roaming');
            if (fastRoaming) fastRoaming.checked = config.fastRoaming;

            const hideSSID = document.getElementById('wifi-hide-ssid');
            if (hideSSID) hideSSID.checked = config.hideSSID;

            // Réseau invité
            const guestEnabled = document.getElementById('guest-enabled');
            if (guestEnabled) guestEnabled.checked = config.guestNetworkEnabled;

            const guestSSID = document.getElementById('guest-ssid');
            if (guestSSID) guestSSID.value = config.guestSSID || '';

            const guestBandwidth = document.getElementById('guest-bandwidth');
            if (guestBandwidth) guestBandwidth.value = config.guestBandwidthLimit || 50;

            // Bandes
            if (config.bands) {
                this.updateBandsUI(config.bands);
            }

            // Calculer le débit max
            const maxSpeed = config.bands ? Math.max(...config.bands.filter(b => b.enabled).map(b => b.maxSpeed || 0)) : 0;
            const maxSpeedEl = document.getElementById('wifi-max-speed');
            if (maxSpeedEl) maxSpeedEl.textContent = `${maxSpeed} Mbps`;

            // Mesh
            const meshEnabled = document.getElementById('mesh-enabled');
            if (meshEnabled) meshEnabled.checked = config.meshEnabled;

            if (config.meshEnabled) {
                await this.loadMeshTopology();
            }

            // Clients
            await this.loadWiFiClients();

        } catch (error) {
            console.error('Error loading WiFi:', error);
        }
    }

    updateBandsUI(bands) {
        const bandMap = { 24: 'Band_2_4GHz', 50: 'Band_5GHz', 60: 'Band_6GHz' };
        
        bands.forEach(band => {
            const bandId = band.band === 'Band_2_4GHz' || band.band === 24 ? '24' :
                          band.band === 'Band_5GHz' || band.band === 50 ? '50' : '60';
            
            const enabledEl = document.getElementById(`band-${bandId}-enabled`);
            if (enabledEl) enabledEl.checked = band.enabled;

            const clientsEl = document.getElementById(`band-${bandId}-clients`);
            if (clientsEl) clientsEl.textContent = band.connectedClients || 0;

            const speedEl = document.getElementById(`band-${bandId}-speed`);
            if (speedEl) speedEl.textContent = band.maxSpeed || 0;

            const channelEl = document.getElementById(`band-${bandId}-channel`);
            if (channelEl) channelEl.textContent = band.channel === 0 ? 'Auto' : band.channel;

            const standardEl = document.getElementById(`band-${bandId}-standard`);
            if (standardEl) {
                const standardNames = {
                    4: 'WiFi 4', 5: 'WiFi 5', 6: 'WiFi 6', 61: 'WiFi 6E', 7: 'WiFi 7'
                };
                standardEl.textContent = standardNames[band.maxStandard] || 'WiFi 6';
            }
        });
    }

    async saveWiFiConfig() {
        const config = {
            globalSSID: document.getElementById('wifi-ssid')?.value,
            globalPassword: document.getElementById('wifi-password')?.value || undefined,
            globalSecurity: parseInt(document.getElementById('wifi-security')?.value) || 4,
            enabled: document.getElementById('wifi-enabled')?.checked ?? true,
            hideSSID: document.getElementById('wifi-hide-ssid')?.checked ?? false,
            smartConnect: document.getElementById('wifi-smart-connect')?.checked ?? true,
            fastRoaming: document.getElementById('wifi-fast-roaming')?.checked ?? true,
            guestNetworkEnabled: document.getElementById('guest-enabled')?.checked ?? false,
            guestSSID: document.getElementById('guest-ssid')?.value,
            guestPassword: document.getElementById('guest-password')?.value || undefined,
            guestBandwidthLimit: parseInt(document.getElementById('guest-bandwidth')?.value) || 50
        };

        try {
            await this.api('wifi/config', {
                method: 'POST',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration WiFi enregistrée', severity: 0 });
            this.loadWiFi();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async toggleWiFi() {
        const enabled = document.getElementById('wifi-enabled')?.checked;
        await this.saveWiFiConfig();
    }

    async toggleBand(bandId) {
        const bandMap = { '24': 24, '50': 50, '60': 60 };
        const band = bandMap[bandId];
        const enabled = document.getElementById(`band-${bandId}-enabled`)?.checked;

        try {
            await this.api(`wifi/bands/${band}`, {
                method: 'PUT',
                body: JSON.stringify({
                    band: band,
                    enabled: enabled,
                    useGlobalSSID: true,
                    channel: 0,
                    channelWidth: bandId === '24' ? 40 : 80,
                    txPower: 100,
                    bandSteering: true,
                    beamforming: true,
                    muMimo: true,
                    ofdma: true
                })
            });
            this.showToast({ title: 'Succès', message: `Bande ${bandId === '24' ? '2.4' : bandId === '50' ? '5' : '6'} GHz ${enabled ? 'activée' : 'désactivée'}`, severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showBandConfig(bandId) {
        const bandNames = { '24': '2.4 GHz', '50': '5 GHz', '60': '6 GHz' };
        const bandMap = { '24': 24, '50': 50, '60': 60 };
        
        document.getElementById('modal-title').innerHTML = `<i class="fas fa-broadcast-tower"></i> Configuration ${bandNames[bandId]}`;
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Canal (0 = Auto)</label>
                <select id="band-channel-select" class="form-control">
                    <option value="0">Auto</option>
                    ${this.getChannelOptions(bandId)}
                </select>
            </div>
            <div class="form-group">
                <label>Largeur de canal</label>
                <select id="band-width-select" class="form-control">
                    <option value="20">20 MHz</option>
                    <option value="40" ${bandId === '24' ? 'selected' : ''}>40 MHz</option>
                    ${bandId !== '24' ? '<option value="80" selected>80 MHz</option>' : ''}
                    ${bandId !== '24' ? '<option value="160">160 MHz</option>' : ''}
                    ${bandId === '60' ? '<option value="320">320 MHz (WiFi 7)</option>' : ''}
                </select>
            </div>
            <div class="form-group">
                <label>Puissance de transmission: <span id="tx-power-value">100</span>%</label>
                <input type="range" id="band-tx-power" class="form-range" min="10" max="100" value="100" 
                       oninput="document.getElementById('tx-power-value').textContent = this.value">
            </div>
            <div class="form-group">
                <label>Standard WiFi maximum</label>
                <select id="band-standard-select" class="form-control">
                    ${bandId === '24' ? '<option value="4">WiFi 4 (N)</option>' : ''}
                    ${bandId === '50' ? '<option value="5">WiFi 5 (AC)</option>' : ''}
                    <option value="6" selected>WiFi 6 (AX)</option>
                    ${bandId === '60' ? '<option value="61">WiFi 6E (AX)</option>' : ''}
                    <option value="7">WiFi 7 (BE)</option>
                </select>
            </div>
            <h4 style="margin-top: 20px;">Fonctionnalités avancées</h4>
            <div class="form-row">
                <div class="form-group">
                    <label class="checkbox-label">
                        <input type="checkbox" id="band-beamforming" checked>
                        Beamforming
                    </label>
                </div>
                <div class="form-group">
                    <label class="checkbox-label">
                        <input type="checkbox" id="band-mumimo" checked>
                        MU-MIMO
                    </label>
                </div>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label class="checkbox-label">
                        <input type="checkbox" id="band-ofdma" checked>
                        OFDMA (WiFi 6+)
                    </label>
                </div>
                <div class="form-group">
                    <label class="checkbox-label">
                        <input type="checkbox" id="band-steering" checked>
                        Band Steering
                    </label>
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.saveBandConfig('${bandId}')">
                <i class="fas fa-save"></i> Enregistrer
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    getChannelOptions(bandId) {
        if (bandId === '24') {
            return [1,2,3,4,5,6,7,8,9,10,11].map(c => 
                `<option value="${c}" ${c === 1 || c === 6 || c === 11 ? 'style="font-weight:bold;"' : ''}>${c}${c === 1 || c === 6 || c === 11 ? ' (Recommandé)' : ''}</option>`
            ).join('');
        } else if (bandId === '50') {
            const channels = [36,40,44,48,52,56,60,64,100,104,108,112,116,120,124,128,132,136,140,144,149,153,157,161,165];
            return channels.map(c => {
                const isDFS = c >= 52 && c <= 144;
                return `<option value="${c}">${c}${isDFS ? ' (DFS)' : ''}</option>`;
            }).join('');
        } else {
            const channels = [];
            for (let i = 1; i <= 233; i += 4) channels.push(i);
            return channels.slice(0, 20).map(c => `<option value="${c}">${c}</option>`).join('');
        }
    }

    async saveBandConfig(bandId) {
        const bandMap = { '24': 24, '50': 50, '60': 60 };
        const config = {
            band: bandMap[bandId],
            enabled: document.getElementById(`band-${bandId}-enabled`)?.checked ?? true,
            useGlobalSSID: true,
            channel: parseInt(document.getElementById('band-channel-select')?.value) || 0,
            channelWidth: parseInt(document.getElementById('band-width-select')?.value) || 80,
            txPower: parseInt(document.getElementById('band-tx-power')?.value) || 100,
            security: parseInt(document.getElementById('wifi-security')?.value) || 4,
            bandSteering: document.getElementById('band-steering')?.checked ?? true,
            beamforming: document.getElementById('band-beamforming')?.checked ?? true,
            muMimo: document.getElementById('band-mumimo')?.checked ?? true,
            ofdma: document.getElementById('band-ofdma')?.checked ?? true
        };

        try {
            await this.api(`wifi/bands/${bandMap[bandId]}`, {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Configuration de la bande enregistrée', severity: 0 });
            this.loadWiFi();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async loadWiFiClients() {
        try {
            const clients = await this.api('wifi/clients');
            const tbody = document.getElementById('wifi-clients-table');
            if (!tbody) return;

            if (!clients || !clients.length) {
                tbody.innerHTML = '<tr><td colspan="7" class="empty-state">Aucun client connecté</td></tr>';
                return;
            }

            tbody.innerHTML = clients.map(client => {
                const signalClass = client.rssi > -50 ? 'success' : client.rssi > -70 ? 'warning' : 'danger';
                const bandName = client.band === 24 || client.band === 'Band_2_4GHz' ? '2.4 GHz' :
                                client.band === 50 || client.band === 'Band_5GHz' ? '5 GHz' : '6 GHz';
                const standardName = { 4: 'WiFi 4', 5: 'WiFi 5', 6: 'WiFi 6', 61: 'WiFi 6E', 7: 'WiFi 7' }[client.standard] || '-';
                
                return `
                    <tr>
                        <td>
                            <div class="device-info">
                                <strong>${this.escapeHtml(client.hostname || client.macAddress)}</strong>
                                <small>${this.escapeHtml(client.ipAddress || '-')}</small>
                            </div>
                        </td>
                        <td><span class="badge">${bandName}</span></td>
                        <td>
                            <span class="signal-indicator ${signalClass}">
                                <i class="fas fa-signal"></i> ${client.rssi} dBm
                            </span>
                        </td>
                        <td>${client.txRate || 0} / ${client.rxRate || 0} Mbps</td>
                        <td><span class="badge">${standardName}</span></td>
                        <td>${client.meshNodeId ? this.escapeHtml(client.meshNodeId) : 'Principal'}</td>
                        <td>${this.formatDuration(client.connectionTime)}</td>
                    </tr>
                `;
            }).join('');
        } catch (error) {
            console.error('Error loading WiFi clients:', error);
        }
    }

    formatDuration(seconds) {
        if (!seconds) return '-';
        if (seconds < 60) return `${seconds}s`;
        if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
        if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
        return `${Math.floor(seconds / 86400)}j`;
    }

    async refreshWiFiClients() {
        await this.loadWiFiClients();
        this.showToast({ title: 'Actualisation', message: 'Liste des clients mise à jour', severity: 0 });
    }

    // Mesh
    async toggleMesh() {
        const enabled = document.getElementById('mesh-enabled')?.checked;
        
        try {
            await this.api('wifi/mesh/config', {
                method: 'POST',
                body: JSON.stringify({
                    enabled: enabled,
                    meshId: 'NetGuard-Mesh',
                    role: 0, // Controller
                    preferredBackhaul: 0, // Auto
                    allowDaisyChain: true,
                    maxHops: 2,
                    autoOptimization: true
                })
            });
            
            if (enabled) {
                await this.loadMeshTopology();
            } else {
                document.getElementById('mesh-empty').style.display = 'block';
                document.getElementById('mesh-nodes-list').style.display = 'none';
            }
            
            this.showToast({ title: 'Mesh', message: enabled ? 'Mode Mesh activé' : 'Mode Mesh désactivé', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async loadMeshTopology() {
        try {
            const [topology, nodes] = await Promise.all([
                this.api('wifi/mesh/topology').catch(() => ({})),
                this.api('wifi/mesh/nodes').catch(() => [])
            ]);

            const emptyEl = document.getElementById('mesh-empty');
            const listEl = document.getElementById('mesh-nodes-list');

            if (!nodes || nodes.length === 0) {
                if (emptyEl) emptyEl.style.display = 'block';
                if (listEl) listEl.style.display = 'none';
                return;
            }

            if (emptyEl) emptyEl.style.display = 'none';
            if (listEl) {
                listEl.style.display = 'grid';
                listEl.innerHTML = nodes.map(node => this.createMeshNodeCard(node)).join('');
            }
        } catch (error) {
            console.error('Error loading mesh topology:', error);
        }
    }

    createMeshNodeCard(node) {
        const statusClass = node.status === 1 || node.status === 'Online' ? 'online' : 'offline';
        const statusText = node.status === 1 || node.status === 'Online' ? 'En ligne' : 'Hors ligne';
        const backhaulIcon = node.backhaulType === 1 || node.backhaulType === 'Ethernet' ? 'fa-ethernet' : 'fa-wifi';
        const roleText = node.role === 0 || node.role === 'Controller' ? 'Contrôleur' : 'Satellite';

        return `
            <div class="mesh-node-card ${statusClass}">
                <div class="mesh-node-header">
                    <i class="fas ${node.role === 0 ? 'fa-server' : 'fa-satellite'}"></i>
                    <div>
                        <h4>${this.escapeHtml(node.name || 'Nœud')}</h4>
                        <span class="mesh-role">${roleText}</span>
                    </div>
                    <span class="status-badge ${statusClass}">${statusText}</span>
                </div>
                <div class="mesh-node-info">
                    <div class="mesh-stat">
                        <i class="fas fa-map-marker-alt"></i>
                        <span>${this.escapeHtml(node.location || 'Non défini')}</span>
                    </div>
                    <div class="mesh-stat">
                        <i class="fas fa-network-wired"></i>
                        <span>${this.escapeHtml(node.ipAddress || '-')}</span>
                    </div>
                    <div class="mesh-stat">
                        <i class="fas ${backhaulIcon}"></i>
                        <span>${node.backhaulSpeed || 0} Mbps</span>
                    </div>
                    <div class="mesh-stat">
                        <i class="fas fa-users"></i>
                        <span>${node.connectedClients || 0} clients</span>
                    </div>
                </div>
                <div class="mesh-backhaul-quality">
                    <span>Qualité backhaul:</span>
                    <div class="progress-bar">
                        <div class="progress-fill" style="width: ${node.backhaulQuality || 0}%; background: ${node.backhaulQuality > 70 ? 'var(--success)' : node.backhaulQuality > 40 ? 'var(--warning)' : 'var(--danger)'}"></div>
                    </div>
                    <span>${node.backhaulQuality || 0}%</span>
                </div>
                ${node.role !== 0 ? `
                    <button class="btn btn-sm btn-danger" onclick="app.removeMeshNode('${node.id}')">
                        <i class="fas fa-trash"></i> Supprimer
                    </button>
                ` : ''}
            </div>
        `;
    }

    showAddMeshNodeModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-satellite"></i> Ajouter un nœud Mesh';
        document.getElementById('modal-body').innerHTML = `
            <p class="text-muted">Ajoutez un nouveau satellite pour étendre la couverture WiFi</p>
            <div class="form-group">
                <label>Adresse MAC du nœud</label>
                <input type="text" id="mesh-node-mac" class="form-control" placeholder="AA:BB:CC:DD:EE:FF">
            </div>
            <div class="form-group">
                <label>Nom</label>
                <input type="text" id="mesh-node-name" class="form-control" placeholder="Satellite Salon">
            </div>
            <div class="form-group">
                <label>Emplacement</label>
                <input type="text" id="mesh-node-location" class="form-control" placeholder="Salon, Étage, etc.">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addMeshNode()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async addMeshNode() {
        const request = {
            macAddress: document.getElementById('mesh-node-mac')?.value,
            name: document.getElementById('mesh-node-name')?.value,
            location: document.getElementById('mesh-node-location')?.value
        };

        try {
            await this.api('wifi/mesh/nodes', {
                method: 'POST',
                body: JSON.stringify(request)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Nœud Mesh ajouté', severity: 0 });
            this.loadMeshTopology();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async removeMeshNode(nodeId) {
        if (!confirm('Supprimer ce nœud Mesh ?')) return;

        try {
            await this.api(`wifi/mesh/nodes/${nodeId}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Nœud supprimé', severity: 0 });
            this.loadMeshTopology();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async optimizeMesh() {
        try {
            await this.api('wifi/mesh/optimize', { method: 'POST' });
            this.showToast({ title: 'Mesh', message: 'Optimisation en cours...', severity: 0 });
            setTimeout(() => this.loadMeshTopology(), 3000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async toggleGuestNetwork() {
        await this.saveWiFiConfig();
    }

    async scanWiFiChannels() {
        const bandId = document.getElementById('channel-scan-band')?.value || '50';
        const bandMap = { '24': 24, '50': 50, '60': 60 };
        const container = document.getElementById('channel-analysis');
        
        if (container) {
            container.innerHTML = '<p class="text-muted"><i class="fas fa-spinner fa-spin"></i> Analyse en cours...</p>';
        }

        try {
            const analysis = await this.api(`wifi/channels/scan/${bandMap[bandId]}`);
            
            if (container && analysis && analysis.length) {
                container.innerHTML = analysis.map(ch => {
                    const utilizationClass = ch.utilization < 30 ? 'success' : ch.utilization < 60 ? 'warning' : 'danger';
                    return `
                        <div class="channel-bar ${ch.isRecommended ? 'recommended' : ''} ${ch.isDFS ? 'dfs' : ''}">
                            <div class="channel-fill" style="height: ${ch.utilization}%; background: var(--${utilizationClass})"></div>
                            <div class="channel-label">
                                <span class="channel-number">${ch.channel}</span>
                                <span class="channel-networks">${ch.networkCount} réseaux</span>
                                ${ch.isRecommended ? '<i class="fas fa-star" title="Recommandé"></i>' : ''}
                                ${ch.isDFS ? '<i class="fas fa-radar" title="Canal DFS"></i>' : ''}
                            </div>
                        </div>
                    `;
                }).join('');
            }
        } catch (error) {
            if (container) {
                container.innerHTML = `<p class="text-danger">Erreur: ${this.escapeHtml(error.message)}</p>`;
            }
        }
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

        // Update title - Titres uniformisés en français
        const titles = {
            dashboard: 'Tableau de bord',
            devices: 'Appareils',
            agents: 'Agents',
            cameras: 'Cameras',
            alerts: 'Journaux',
            traffic: 'Trafic reseau',
            pihole: 'Pi-hole',
            dhcp: 'Serveur DHCP',
            wifi: 'WiFi Multi-Bandes',
            protocols: 'Protocoles Réseau',
            setup: 'Installation',
            sniffer: 'Analyse de paquets',
            router: 'Regles de securite',
            settings: 'Parametres',
            admin: 'Administration',
            parental: 'Controle parental',
            reports: 'Rapports'
        };
        document.getElementById('page-title').textContent = titles[page] || page;

        this.currentPage = page;

        // Load page data
        switch (page) {
            case 'dashboard': this.loadDashboard(); break;
            case 'devices': this.loadDevices(); break;
            case 'agents': this.loadAgents(); break;
            case 'alerts': this.loadAlerts(); break;
            case 'pihole': this.loadPihole(); break;
            case 'parental': this.loadParental(); break;
            case 'cameras': this.loadCameras(); break;
            case 'traffic': this.loadTraffic(); break;
            case 'dhcp': this.loadDhcp(); break;
            case 'wifi': this.loadWiFi(); break;
            case 'protocols': this.loadProtocols(); break;
            case 'sniffer': this.loadSniffer(); break;
            case 'router': this.loadRouter(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }
}

// Instanciation de l'application au chargement de la page
const app = new FirewallApp();
