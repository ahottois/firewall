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
                this.api('alerts?count=5').catch(() => [])
            ]);

            // Update traffic overview
            this.updateTrafficCharts(security);
            this.updateTopThreats(security.recentThreats || []);
            this.updateFirewallRules();
            this.updateSystemStatus(security);

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
            const config = await this.api('dhcp/config').catch(() => ({}));
            const leases = await this.api('dhcp/leases').catch(() => []);
            
            // Remplir la configuration
            document.getElementById('dhcp-enabled').checked = config.enabled || false;
            document.getElementById('dhcp-start').value = config.rangeStart || '';
            document.getElementById('dhcp-end').value = config.rangeEnd || '';
            document.getElementById('dhcp-mask').value = config.subnetMask || '255.255.255.0';
            document.getElementById('dhcp-gateway').value = config.gateway || '';
            document.getElementById('dhcp-dns1').value = config.dns1 || '';
            document.getElementById('dhcp-dns2').value = config.dns2 || '';
            document.getElementById('dhcp-lease').value = config.leaseTime || 1440;
            
            // Afficher les baux
            const tbody = document.getElementById('dhcp-leases-table');
            if (tbody) {
                if (!leases.length) {
                    tbody.innerHTML = '<tr><td colspan="4" class="empty-state">Aucun bail actif</td></tr>';
                } else {
                    tbody.innerHTML = leases.map(lease => `
                        <tr>
                            <td>${this.escapeHtml(lease.ipAddress)}</td>
                            <td>${this.escapeHtml(lease.macAddress)}</td>
                            <td>${this.escapeHtml(lease.hostname || '-')}</td>
                            <td>${this.formatDate(lease.expiration)}</td>
                        </tr>
                    `).join('');
                }
            }
        } catch (error) {
            console.error('Error loading DHCP:', error);
        }
    }

    async loadSniffer() {
        // Vérifier si le sniffer est actif
        try {
            const status = await this.api('sniffer/status').catch(() => ({ isRunning: false }));
            
            const startBtn = document.getElementById('btn-start-sniffer');
            const stopBtn = document.getElementById('btn-stop-sniffer');
            
            if (startBtn && stopBtn) {
                startBtn.style.display = status.isRunning ? 'none' : 'inline-flex';
                stopBtn.style.display = status.isRunning ? 'inline-flex' : 'none';
            }
        } catch (error) {
            console.error('Error loading sniffer status:', error);
        }
    }

    async startSniffer() {
        try {
            await this.api('sniffer/start', { method: 'POST' });
            document.getElementById('btn-start-sniffer').style.display = 'none';
            document.getElementById('btn-stop-sniffer').style.display = 'inline-flex';
            this.showToast({ title: 'Sniffer', message: 'Capture démarrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async stopSniffer() {
        try {
            await this.api('sniffer/stop', { method: 'POST' });
            document.getElementById('btn-start-sniffer').style.display = 'inline-flex';
            document.getElementById('btn-stop-sniffer').style.display = 'none';
            this.showToast({ title: 'Sniffer', message: 'Capture arrêtée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    clearSnifferPackets() {
        const tbody = document.getElementById('sniffer-packets-table');
        if (tbody) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">En attente de paquets...</td></tr>';
        }
    }

    async loadRouter() {
        await this.loadRouterInterfaces();
        await this.loadRouterMappings();
    }

    async loadRouterInterfaces() {
        try {
            const interfaces = await this.api('settings/interfaces').catch(() => [];
            const container = document.getElementById('router-interfaces-list');
            
            if (!container) return;
            
            if (!interfaces.length) {
                container.innerHTML = '<p class="empty-state">Aucune interface détectée</p>';
                return;
            }
            
            container.innerHTML = interfaces.map(iface => `
                <div class="interface-item">
                    <div class="interface-icon">
                        <i class="fas ${iface.isUp ? 'fa-network-wired' : 'fa-times-circle'}"></i>
                    </div>
                    <div class="interface-info">
                        <strong>${this.escapeHtml(iface.name)}</strong>
                        <br>
                        <small>
                            IP: ${this.escapeHtml(iface.ipAddress || 'Non configurée')} |
                            MAC: ${this.escapeHtml(iface.macAddress || '-')} |
                            ${iface.isUp ? '<span class="text-success">Actif</span>' : '<span class="text-danger">Inactif</span>'}
                        </small>
                    </div>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading router interfaces:', error);
        }
    }

    async loadRouterMappings() {
        try {
            const mappings = await this.api('router/mappings').catch(() => [];
            const tbody = document.getElementById('router-mappings-table');
            
            if (!tbody) return;
            
            if (!mappings.length) {
                tbody.innerHTML = '<tr><td colspan="5" class="empty-state">Aucune règle de transfert</td></tr>';
                return;
            }
            
            tbody.innerHTML = mappings.map(mapping => `
                <tr>
                    <td>${this.escapeHtml(mapping.name || '-')}</td>
                    <td>${mapping.localPort}</td>
                    <td>${this.escapeHtml(mapping.targetIp)}:${mapping.targetPort}</td>
                    <td>${this.escapeHtml(mapping.protocol)}</td>
                    <td>
                        <button class="btn btn-sm btn-danger" onclick="app.deleteMapping(${mapping.id})">
                            <i class="fas fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading router mappings:', error);
        }
    }

    async loadSettings() {
        try {
            // Charger les informations système
            const sysInfo = await this.api('settings/system-info').catch(() => ({}));
            const interfaces = await this.api('settings/interfaces').catch(() => [];
            
            // Afficher les informations système
            const sysInfoDiv = document.getElementById('system-info');
            if (sysInfoDiv) {
                sysInfoDiv.innerHTML = `
                    <div class="detail-row"><strong>OS:</strong> ${this.escapeHtml(sysInfo.osDescription || '-')}</div>
                    <div class="detail-row"><strong>Machine:</strong> ${this.escapeHtml(sysInfo.machineName || '-')}</div>
                    <div class="detail-row"><strong>Processeurs:</strong> ${sysInfo.processorCount || '-'}</div>
                    <div class="detail-row"><strong>.NET:</strong> ${this.escapeHtml(sysInfo.dotnetVersion || '-')}</div>
                    <div class="detail-row"><strong>Mémoire:</strong> ${sysInfo.totalMemoryMb ? Math.round(sysInfo.totalMemoryMb / 1024) + ' GB' : '-'}</div>
                `;
            }
            
            // Afficher les interfaces
            const interfacesDiv = document.getElementById('interfaces-list');
            if (interfacesDiv) {
                interfacesDiv.innerHTML = interfaces.map(iface => `
                    <div class="interface-item">
                        <div class="interface-icon">
                            <i class="fas ${iface.isUp ? 'fa-check-circle text-success' : 'fa-times-circle text-danger'}"></i>
                        </div>
                        <div class="interface-info">
                            <strong>${this.escapeHtml(iface.name)}</strong>
                            ${iface.description ? `<br><small>${this.escapeHtml(iface.description)}</small>` : ''}
                            <br>
                            <small>
                                IP: ${this.escapeHtml(iface.ipAddress || '-')} |
                                MAC: ${this.escapeHtml(iface.macAddress || '-')}
                            </small>
                        </div>
                    </div>
                `).join('');
            }
        } catch (error) {
            console.error('Error loading settings:', error);
        }
    }

    async loadAdmin() {
        try {
            // Charger le statut du service
            const status = await this.api('admin/service/status').catch(() => ({ status: 'unknown' }));
            const version = await this.api('admin/version').catch(() => ({ version: '-' }));
            
            // Mettre à jour l'affichage
            const statusText = document.getElementById('service-status-text');
            const statusCard = document.getElementById('service-status-card');
            const versionText = document.getElementById('app-version');
            
            if (statusText) {
                statusText.textContent = status.status === 'running' ? 'En cours' : 'Arrêté';
            }
            if (statusCard) {
                statusCard.classList.remove('success', 'danger');
                statusCard.classList.add(status.status === 'running' ? 'success' : 'danger');
            }
            if (versionText) {
                versionText.textContent = version.version || '1.0.0';
            }
        } catch (error) {
            console.error('Error loading admin:', error);
        }
    }

    async startService() {
        try {
            await this.api('admin/service/start', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service démarré', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async stopService() {
        try {
            await this.api('admin/service/stop', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service arrêté', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async restartService() {
        try {
            await this.api('admin/service/restart', { method: 'POST' });
            this.showToast({ title: 'Succès', message: 'Service redémarré', severity: 0 });
            setTimeout(() => this.loadAdmin(), 2000);
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async installService() {
        try {
            const result = await this.api('admin/service/install', { method: 'POST' });
            this.showToast({ title: 'Succès', message: result.message || 'Service installé', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async uninstallService() {
        if (!confirm('Désinstaller le service ?')) return;
        try {
            const result = await this.api('admin/service/uninstall', { method: 'POST' });
            this.showToast({ title: 'Succès', message: result.message || 'Service désinstallé', severity: 0 });
            this.loadAdmin();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async loadServiceLogs() {
        try {
            const logs = await this.api('admin/logs?lines=100');
            const logsDiv = document.getElementById('service-logs');
            if (logsDiv) {
                logsDiv.innerHTML = logs.map(log => {
                    const levelClass = log.level?.toLowerCase() || '';
                    return `<div class="log-entry ${levelClass}">
                        <span class="log-time">${this.formatDate(log.timestamp)}</span>
                        <span class="log-message">${this.escapeHtml(log.message)}</span>
                    </div>`;
                }).join('');
                logsDiv.scrollTop = logsDiv.scrollHeight;
            }
        } catch (error) {
            console.error('Error loading service logs:', error);
        }
    }

    async checkForUpdates() {
        const statusDiv = document.getElementById('update-status');
        if (statusDiv) {
            statusDiv.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Vérification...';
        }
        
        try {
            const result = await this.api('admin/updates/check');
            if (statusDiv) {
                if (result.updateAvailable) {
                    statusDiv.innerHTML = `<span class="text-warning"><i class="fas fa-exclamation-circle"></i> Mise à jour disponible: v${result.latestVersion}</span>`;
                } else {
                    statusDiv.innerHTML = '<span class="text-success"><i class="fas fa-check-circle"></i> Vous êtes à jour</span>';
                }
            }
        } catch (error) {
            if (statusDiv) {
                statusDiv.innerHTML = '<span class="text-danger"><i class="fas fa-times-circle"></i> Erreur de vérification</span>';
            }
        }
    }

    async updateFromGithub() {
        if (!confirm('Mettre à jour depuis GitHub ? L\'application va redémarrer.')) return;
        
        try {
            await this.api('admin/updates/apply', { method: 'POST' });
            this.showToast({ title: 'Mise à jour', message: 'Mise à jour en cours, l\'application va redémarrer...', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }
    
    showAddMappingModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-plus"></i> Nouvelle règle de transfert';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Nom</label>
                <input type="text" id="mapping-name" class="form-control" placeholder="Ex: Serveur Web">
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>Port local</label>
                    <input type="number" id="mapping-local-port" class="form-control" placeholder="80">
                </div>
                <div class="form-group">
                    <label>Protocole</label>
                    <select id="mapping-protocol" class="form-control">
                        <option value="TCP">TCP</option>
                        <option value="UDP">UDP</option>
                        <option value="BOTH">TCP + UDP</option>
                    </select>
                </div>
            </div>
            <div class="form-row">
                <div class="form-group">
                    <label>IP cible</label>
                    <input type="text" id="mapping-target-ip" class="form-control" placeholder="192.168.1.100">
                </div>
                <div class="form-group">
                    <label>Port cible</label>
                    <input type="number" id="mapping-target-port" class="form-control" placeholder="80">
                </div>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.saveMapping()">
                <i class="fas fa-save"></i> Enregistrer
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async saveMapping() {
        const data = {
            name: document.getElementById('mapping-name').value,
            localPort: parseInt(document.getElementById('mapping-local-port').value),
            targetIp: document.getElementById('mapping-target-ip').value,
            targetPort: parseInt(document.getElementById('mapping-target-port').value),
            protocol: document.getElementById('mapping-protocol').value
        };

        if (!data.localPort || !data.targetIp || !data.targetPort) {
            this.showToast({ title: 'Erreur', message: 'Veuillez remplir tous les champs obligatoires', severity: 2 });
            return;
        }

        try {
            await this.api('router/mappings', {
                method: 'POST',
                body: JSON.stringify(data)
            });
            document.getElementById('modal').classList.remove('active');
            this.loadRouterMappings();
            this.showToast({ title: 'Succès', message: 'Règle ajoutée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async deleteMapping(id) {
        if (!confirm('Supprimer cette règle ?')) return;
        
        try {
            await this.api(`router/mappings/${id}`, { method: 'DELETE' });
            this.loadRouterMappings();
            this.showToast({ title: 'Succès', message: 'Règle supprimée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    showAddDeviceModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-plus"></i> Ajouter un appareil';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Adresse MAC *</label>
                <input type="text" id="new-device-mac" class="form-control" placeholder="AA:BB:CC:DD:EE:FF">
            </div>
            <div class="form-group">
                <label>Adresse IP</label>
                <input type="text" id="new-device-ip" class="form-control" placeholder="192.168.1.100">
            </div>
            <div class="form-group">
                <label>Description</label>
                <input type="text" id="new-device-description" class="form-control" placeholder="Ex: PC Bureau">
            </div>
            <div class="form-group">
                <label class="checkbox-label">
                    <input type="checkbox" id="new-device-trusted"> Appareil de confiance
                </label>
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.saveNewDevice()">
                <i class="fas fa-save"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    }

    async saveNewDevice() {
        const mac = document.getElementById('new-device-mac').value.trim().toUpperCase();
        
        if (!mac) {
            this.showToast({ title: 'Erreur', message: 'L\'adresse MAC est requise', severity: 2 });
            return;
        }
        
        // Valider le format MAC
        const macRegex = /^([0-9A-F]{2}:){5}[0-9A-F]{2}$/;
        if (!macRegex.test(mac)) {
            this.showToast({ title: 'Erreur', message: 'Format MAC invalide (ex: AA:BB:CC:DD:EE:FF)', severity: 2 });
            return;
        }

        const data = {
            macAddress: mac,
            ipAddress: document.getElementById('new-device-ip').value.trim() || null,
            description: document.getElementById('new-device-description').value.trim() || null,
            isTrusted: document.getElementById('new-device-trusted').checked
        };

        try {
            await this.api('devices', {
                method: 'POST',
                body: JSON.stringify(data)
            });
            document.getElementById('modal').classList.remove('active');
            this.loadDevices();
            this.showToast({ title: 'Succès', message: 'Appareil ajouté', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    async saveDhcpConfig() {
        const config = {
            enabled: document.getElementById('dhcp-enabled').checked,
            rangeStart: document.getElementById('dhcp-start').value,
            rangeEnd: document.getElementById('dhcp-end').value,
            subnetMask: document.getElementById('dhcp-mask').value,
            gateway: document.getElementById('dhcp-gateway').value,
            dns1: document.getElementById('dhcp-dns1').value,
            dns2: document.getElementById('dhcp-dns2').value,
            leaseTime: parseInt(document.getElementById('dhcp-lease').value) || 1440
        };

        try {
            await this.api('dhcp/config', {
                method: 'POST',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration DHCP enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }

    loadDashboardStats() {
        // Alias pour loadDashboard
        this.loadDashboard();
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
            case 'sniffer': this.loadSniffer(); break;
            case 'router': this.loadRouter(); break;
            case 'settings': this.loadSettings(); break;
            case 'admin': this.loadAdmin(); break;
        }
    }
}
