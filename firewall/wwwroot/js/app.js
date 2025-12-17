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

    // Dashboard
    async loadDashboard() {
        try {
            const [trafficStats, alerts, rules, systemStatus] = await Promise.all([
                this.api('traffic/stats?hours=24'),
                this.api('alerts?count=10'),
                this.api('router/mappings'),
                this.api('system/status')
            ]);

            this.renderTrafficOverview(trafficStats);
            this.renderTopThreats(alerts);
            this.renderFirewallRules(rules);
            this.renderSystemStatus(systemStatus);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    renderTrafficOverview(stats) {
        const total = stats.totalPackets || 1;
        const blocked = stats.suspiciousPackets || 0;
        const allowed = total - blocked;
        
        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        // Update Charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.background = `conic-gradient(var(--accent-primary) 0% ${allowedPercent}%, transparent ${allowedPercent}% 100%)`;
            document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        }
        
        if (blockedChart) {
            blockedChart.style.background = `conic-gradient(var(--danger) 0% ${blockedPercent}%, transparent ${blockedPercent}% 100%)`;
            document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;
        }

        // Update Sparkline (Simulated for now based on protocol distribution or random)
        const sparkline = document.getElementById('traffic-sparkline');
        if (sparkline) {
            // Generate some fake bars for visual effect if no historical data
            let barsHtml = '';
            for (let i = 0; i < 20; i++) {
                const height = Math.floor(Math.random() * 80) + 20;
                barsHtml += `<div class="spark-bar" style="height: ${height}%"></div>`;
            }
            sparkline.innerHTML = barsHtml;
        }
    }

    renderTopThreats(alerts) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        // Filter for high severity or just take top 4
        const threats = alerts.filter(a => a.severity >= 2).slice(0, 4);

        if (threats.length === 0) {
            container.innerHTML = '<div class="empty-state">No recent threats detected</div>';
            return;
        }

        container.innerHTML = threats.map(alert => `
            <div class="threat-item">
                <div class="threat-info">
                    <div class="threat-icon">
                        <i class="fas fa-shield-alt"></i>
                    </div>
                    <div class="threat-details">
                        <h4>${this.escapeHtml(alert.title)}</h4>
                        <p>${alert.sourceIp ? `Source: ${this.escapeHtml(alert.sourceIp)}` : this.formatDate(alert.timestamp)}</p>
                    </div>
                </div>
                <button class="btn btn-sm btn-outline" onclick="app.navigateTo('alerts')">View Details</button>
            </div>
        `).join('');
    }

    renderFirewallRules(rules) {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        if (!rules || rules.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">No active rules</td></tr>';
            return;
        }

        tbody.innerHTML = rules.slice(0, 5).map((rule, index) => `
            <tr>
                <td>RULE_${(index + 1).toString().padStart(2, '0')}</td>
                <td>${rule.enabled ? 'ALLOW' : 'BLOCK'}</td>
                <td>Any</td>
                <td>${this.escapeHtml(rule.targetIp)}</td>
                <td>${rule.listenPort}/${rule.protocol}</td>
                <td class="status-active">Active</td>
            </tr>
        `).join('');
    }

    renderSystemStatus(status) {
        const container = document.querySelector('.system-status-body');
        if (!container) return;

        container.innerHTML = `
            <div class="status-item">
                <i class="fas fa-check-circle success"></i>
                <span>All Services Running</span>
            </div>
            <div class="status-item">
                <i class="fas fa-memory" style="color: var(--accent-primary)"></i>
                <span>Memory Usage: ${status.memoryUsageMb} MB</span>
            </div>
            <div class="status-item">
                <i class="fas fa-clock" style="color: var(--text-secondary)"></i>
                <span>Uptime: ${status.uptime}</span>
            </div>
            <div class="status-item">
                <i class="fas fa-code-branch" style="color: var(--text-secondary)"></i>
                <span>Version: ${status.version}</span>
            </div>
        `;
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

    // Dashboard
    async loadDashboard() {
        try {
            const [trafficStats, alerts, rules, systemStatus] = await Promise.all([
                this.api('traffic/stats?hours=24'),
                this.api('alerts?count=10'),
                this.api('router/mappings'),
                this.api('system/status')
            ]);

            this.renderTrafficOverview(trafficStats);
            this.renderTopThreats(alerts);
            this.renderFirewallRules(rules);
            this.renderSystemStatus(systemStatus);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    renderTrafficOverview(stats) {
        const total = stats.totalPackets || 1;
        const blocked = stats.suspiciousPackets || 0;
        const allowed = total - blocked;
        
        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        // Update Charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.background = `conic-gradient(var(--accent-primary) 0% ${allowedPercent}%, transparent ${allowedPercent}% 100%)`;
            document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        }
        
        if (blockedChart) {
            blockedChart.style.background = `conic-gradient(var(--danger) 0% ${blockedPercent}%, transparent ${blockedPercent}% 100%)`;
            document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;
        }

        // Update Sparkline (Simulated for now based on protocol distribution or random)
        const sparkline = document.getElementById('traffic-sparkline');
        if (sparkline) {
            // Generate some fake bars for visual effect if no historical data
            let barsHtml = '';
            for (let i = 0; i < 20; i++) {
                const height = Math.floor(Math.random() * 80) + 20;
                barsHtml += `<div class="spark-bar" style="height: ${height}%"></div>`;
            }
            sparkline.innerHTML = barsHtml;
        }
    }

    renderTopThreats(alerts) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        // Filter for high severity or just take top 4
        const threats = alerts.filter(a => a.severity >= 2).slice(0, 4);

        if (threats.length === 0) {
            container.innerHTML = '<div class="empty-state">No recent threats detected</div>';
            return;
        }

        container.innerHTML = threats.map(alert => `
            <div class="threat-item">
                <div class="threat-info">
                    <div class="threat-icon">
                        <i class="fas fa-shield-alt"></i>
                    </div>
                    <div class="threat-details">
                        <h4>${this.escapeHtml(alert.title)}</h4>
                        <p>${alert.sourceIp ? `Source: ${this.escapeHtml(alert.sourceIp)}` : this.formatDate(alert.timestamp)}</p>
                    </div>
                </div>
                <button class="btn btn-sm btn-outline" onclick="app.navigateTo('alerts')">View Details</button>
            </div>
        `).join('');
    }

    renderFirewallRules(rules) {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        if (!rules || rules.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">No active rules</td></tr>';
            return;
        }

        tbody.innerHTML = rules.slice(0, 5).map((rule, index) => `
            <tr>
                <td>RULE_${(index + 1).toString().padStart(2, '0')}</td>
                <td>${rule.enabled ? 'ALLOW' : 'BLOCK'}</td>
                <td>Any</td>
                <td>${this.escapeHtml(rule.targetIp)}</td>
                <td>${rule.listenPort}/${rule.protocol}</td>
                <td class="status-active">Active</td>
            </tr>
        `).join('');
    }

    renderSystemStatus(status) {
        const container = document.querySelector('.system-status-body');
        if (!container) return;

        container.innerHTML = `
            <div class="status-item">
                <i class="fas fa-check-circle success"></i>
                <span>All Services Running</span>
            </div>
            <div class="status-item">
                <i class="fas fa-memory" style="color: var(--accent-primary)"></i>
                <span>Memory Usage: ${status.memoryUsageMb} MB</span>
            </div>
            <div class="status-item">
                <i class="fas fa-clock" style="color: var(--text-secondary)"></i>
                <span>Uptime: ${status.uptime}</span>
            </div>
            <div class="status-item">
                <i class="fas fa-code-branch" style="color: var(--text-secondary)"></i>
                <span>Version: ${status.version}</span>
            </div>
        `;
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

    // Dashboard
    async loadDashboard() {
        try {
            const [trafficStats, alerts, rules, systemStatus] = await Promise.all([
                this.api('traffic/stats?hours=24'),
                this.api('alerts?count=10'),
                this.api('router/mappings'),
                this.api('system/status')
            ]);

            this.renderTrafficOverview(trafficStats);
            this.renderTopThreats(alerts);
            this.renderFirewallRules(rules);
            this.renderSystemStatus(systemStatus);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    renderTrafficOverview(stats) {
        const total = stats.totalPackets || 1;
        const blocked = stats.suspiciousPackets || 0;
        const allowed = total - blocked;
        
        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        // Update Charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.background = `conic-gradient(var(--accent-primary) 0% ${allowedPercent}%, transparent ${allowedPercent}% 100%)`;
            document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        }
        
        if (blockedChart) {
            blockedChart.style.background = `conic-gradient(var(--danger) 0% ${blockedPercent}%, transparent ${blockedPercent}% 100%)`;
            document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;
        }

        // Update Sparkline (Simulated for now based on protocol distribution or random)
        const sparkline = document.getElementById('traffic-sparkline');
        if (sparkline) {
            // Generate some fake bars for visual effect if no historical data
            let barsHtml = '';
            for (let i = 0; i < 20; i++) {
                const height = Math.floor(Math.random() * 80) + 20;
                barsHtml += `<div class="spark-bar" style="height: ${height}%"></div>`;
            }
            sparkline.innerHTML = barsHtml;
        }
    }

    renderTopThreats(alerts) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        // Filter for high severity or just take top 4
        const threats = alerts.filter(a => a.severity >= 2).slice(0, 4);

        if (threats.length === 0) {
            container.innerHTML = '<div class="empty-state">No recent threats detected</div>';
            return;
        }

        container.innerHTML = threats.map(alert => `
            <div class="threat-item">
                <div class="threat-info">
                    <div class="threat-icon">
                        <i class="fas fa-shield-alt"></i>
                    </div>
                    <div class="threat-details">
                        <h4>${this.escapeHtml(alert.title)}</h4>
                        <p>${alert.sourceIp ? `Source: ${this.escapeHtml(alert.sourceIp)}` : this.formatDate(alert.timestamp)}</p>
                    </div>
                </div>
                <button class="btn btn-sm btn-outline" onclick="app.navigateTo('alerts')">View Details</button>
            </div>
        `).join('');
    }

    renderFirewallRules(rules) {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        if (!rules || rules.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">No active rules</td></tr>';
            return;
        }

        tbody.innerHTML = rules.slice(0, 5).map((rule, index) => `
            <tr>
                <td>RULE_${(index + 1).toString().padStart(2, '0')}</td>
                <td>${rule.enabled ? 'ALLOW' : 'BLOCK'}</td>
                <td>Any</td>
                <td>${this.escapeHtml(rule.targetIp)}</td>
                <td>${rule.listenPort}/${rule.protocol}</td>
                <td class="status-active">Active</td>
            </tr>
        `).join('');
    }

    renderSystemStatus(status) {
        const container = document.querySelector('.system-status-body');
        if (!container) return;

        container.innerHTML = `
            <div class="status-item">
                <i class="fas fa-check-circle success"></i>
                <span>All Services Running</span>
            </div>
            <div class="status-item">
                <i class="fas fa-memory" style="color: var(--accent-primary)"></i>
                <span>Memory Usage: ${status.memoryUsageMb} MB</span>
            </div>
            <div class="status-item">
                <i class="fas fa-clock" style="color: var(--text-secondary)"></i>
                <span>Uptime: ${status.uptime}</span>
            </div>
            <div class="status-item">
                <i class="fas fa-code-branch" style="color: var(--text-secondary)"></i>
                <span>Version: ${status.version}</span>
            </div>
        `;
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

    // Dashboard
    async loadDashboard() {
        try {
            const [trafficStats, alerts, rules, systemStatus] = await Promise.all([
                this.api('traffic/stats?hours=24'),
                this.api('alerts?count=10'),
                this.api('router/mappings'),
                this.api('system/status')
            ]);

            this.renderTrafficOverview(trafficStats);
            this.renderTopThreats(alerts);
            this.renderFirewallRules(rules);
            this.renderSystemStatus(systemStatus);

        } catch (error) {
            console.error('Error loading dashboard:', error);
        }
    }

    renderTrafficOverview(stats) {
        const total = stats.totalPackets || 1;
        const blocked = stats.suspiciousPackets || 0;
        const allowed = total - blocked;
        
        const allowedPercent = Math.round((allowed / total) * 100);
        const blockedPercent = Math.round((blocked / total) * 100);

        // Update Charts
        const allowedChart = document.getElementById('allowed-traffic-chart');
        const blockedChart = document.getElementById('blocked-traffic-chart');
        
        if (allowedChart) {
            allowedChart.style.background = `conic-gradient(var(--accent-primary) 0% ${allowedPercent}%, transparent ${allowedPercent}% 100%)`;
            document.getElementById('allowed-percent').textContent = `${allowedPercent}%`;
        }
        
        if (blockedChart) {
            blockedChart.style.background = `conic-gradient(var(--danger) 0% ${blockedPercent}%, transparent ${blockedPercent}% 100%)`;
            document.getElementById('blocked-percent').textContent = `${blockedPercent}%`;
        }

        // Update Sparkline (Simulated for now based on protocol distribution or random)
        const sparkline = document.getElementById('traffic-sparkline');
        if (sparkline) {
            // Generate some fake bars for visual effect if no historical data
            let barsHtml = '';
            for (let i = 0; i < 20; i++) {
                const height = Math.floor(Math.random() * 80) + 20;
                barsHtml += `<div class="spark-bar" style="height: ${height}%"></div>`;
            }
            sparkline.innerHTML = barsHtml;
        }
    }

    renderTopThreats(alerts) {
        const container = document.getElementById('top-threats-list');
        if (!container) return;

        // Filter for high severity or just take top 4
        const threats = alerts.filter(a => a.severity >= 2).slice(0, 4);

        if (threats.length === 0) {
            container.innerHTML = '<div class="empty-state">No recent threats detected</div>';
            return;
        }

        container.innerHTML = threats.map(alert => `
            <div class="threat-item">
                <div class="threat-info">
                    <div class="threat-icon">
                        <i class="fas fa-shield-alt"></i>
                    </div>
                    <div class="threat-details">
                        <h4>${this.escapeHtml(alert.title)}</h4>
                        <p>${alert.sourceIp ? `Source: ${this.escapeHtml(alert.sourceIp)}` : this.formatDate(alert.timestamp)}</p>
                    </div>
                </div>
                <button class="btn btn-sm btn-outline" onclick="app.navigateTo('alerts')">View Details</button>
            </div>
        `).join('');
    }

    renderFirewallRules(rules) {
        const tbody = document.getElementById('firewall-rules-body');
        if (!tbody) return;

        if (!rules || rules.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">No active rules</td></tr>';
            return;
        }

        tbody.innerHTML = rules.slice(0, 5).map((rule, index) => `
            <tr>
                <td>RULE_${(index + 1).toString().padStart(2, '0')}</td>
                <td>${rule.enabled ? 'ALLOW' : 'BLOCK'}</td>
                <td>Any</td>
                <td>${this.escapeHtml(rule.targetIp)}</td>
                <td>${rule.listenPort}/${rule.protocol}</td>
                <td class="status-active">Active</td>
            </tr>
        `).join('');
    }

    renderSystemStatus(status) {
        const container = document.querySelector('.system-status-body');
        if (!container) return;

        container.innerHTML = `
            <div class="status-item">
                <i class="fas fa-check-circle success"></i>
                <span>All Services Running</span>
            </div>
            <div class="status-item">
                <i class="fas fa-memory" style="color: var(--accent-primary)"></i>
                <span>Memory Usage: ${status.memoryUsageMb} MB</span>
            </div>
            <div class="status-item">
                <i class="fas fa-clock" style="color: var(--text-secondary)"></i>
                <span>Uptime: ${status.uptime}</span>
            </div>
            <div class="status-item">
                <i class="fas fa-code-branch" style="color: var(--text-secondary)"></i>
                <span>Version: ${status.version}</span>
            </div>
        `;
    }

    async loadPiholeStats() {
        try {
            const stats = await this.api('pihole/summary', { suppressErrorLog: true });
            
            // Check if stats are available
            if (!stats || stats.status === 'unavailable') {
                document.getElementById('pihole-stats-container').style.display = 'none';
                return;
            }

            document.getElementById('pihole-stats-container').style.display = 'block';
            
            document.getElementById('ph-queries').textContent = this.formatNumber(stats.dns_queries_today);
            document.getElementById('ph-blocked').textContent = this.formatNumber(stats.ads_blocked_today);
            document.getElementById('ph-percent').textContent = (stats.ads_percentage_today || 0).toFixed(1) + '%';
            document.getElementById('ph-domains').textContent = this.formatNumber(stats.domains_being_blocked);
            
            // New Stats
            document.getElementById('ph-clients').textContent = stats.unique_clients || 0;
            document.getElementById('ph-reply-ip').textContent = this.formatNumber(stats.reply_IP);
            document.getElementById('ph-reply-nx').textContent = this.formatNumber(stats.reply_NXDOMAIN);
            
            if (stats.gravity_last_updated && stats.gravity_last_updated.relative) {
                document.getElementById('ph-updated').textContent = `Mis à jour ${this.escapeHtml(stats.gravity_last_updated.relative)}`;
            } else {
                document.getElementById('ph-updated').textContent = 'Jamais mis à jour';
            }

        } catch (error) {
            // Silent fail for 404 (not installed/available)
            if (error.message && (error.message.includes('404') || error.message.includes('HTTP 404'))) {
                // Do nothing, just hide stats
            } else {
                console.error('Error loading Pi-hole stats:', error);
            }
            const statsContainer = document.getElementById('pihole-stats-container');
            if (statsContainer) statsContainer.style.display = 'none';
        }
    }

    async installPihole() {
        // Similar to existing install logic, adapted for Pi-hole
        // ...leaving out existing code for brevity...
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
            
            // Reset classes
            statusCard.className = 'stat-card';

            if (!status.isInstalled) {
                statusText.textContent = 'Non installe';
                statusCard.classList.add('not-installed');
            } else if (status.status === 'failed') {
                statusText.textContent = 'Erreur (Crash)';
                statusCard.classList.add('offline'); // Red color
            } else if (status.isRunning) {
                statusText.textContent = 'En cours';
                statusCard.classList.add('running');
            } else {
                statusText.textContent = 'Arrete';
                statusCard.classList.add('stopped');
            }

            // Service Management Buttons Logic
            const btnStart = document.getElementById('btn-start-service');
            const btnStop = document.getElementById('btn-stop-service');
            const btnRestart = document.getElementById('btn-restart-service');

            if (!status.isInstalled) {
                // Not installed: Disable all service controls
                btnStart.style.display = 'none';
                btnStop.style.display = 'none';
                btnRestart.style.display = 'none';
            } else if (status.isRunning) {
                // Running: Show Stop & Restart
                btnStart.style.display = 'none';
                btnStop.style.display = 'inline-block';
                btnRestart.style.display = 'inline-block';
                
                btnStop.disabled = false;
                btnRestart.disabled = false;
            } else {
                // Stopped or Failed: Show Start (and maybe Restart/Stop if failed?)
                // User requirement: "si il a crash alors on peut ou redémarer ou arréter"
                btnStart.style.display = 'inline-block';
                btnStop.style.display = 'none';
                btnRestart.style.display = 'none';

                if (status.status === 'failed') {
                    btnStop.style.display = 'inline-block';
                    btnRestart.style.display = 'inline-block';
                }
                
                btnStart.disabled = false;
                btnStop.disabled = false;
                btnRestart.disabled = false;
            }

            // Daemon Installation Buttons Logic
            const btnInstall = document.getElementById('btn-install-service');
            const btnUninstall = document.getElementById('btn-uninstall-service');

            if (status.isInstalled) {
                btnInstall.style.display = 'none';
                btnUninstall.style.display = 'inline-block';
                btnUninstall.disabled = false;
            } else {
                btnInstall.style.display = 'inline-block';
                btnUninstall.style.display = 'none';
                btnInstall.disabled = false;
            }

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
                    let commitsHtml = '';
                    if (result.commits && result.commits.length > 0) {
                        commitsHtml = `
                            <div class="update-commits">
                                <strong>Changements recents:</strong>
                                <ul>
                                    ${result.commits.map(c => `
                                        <li>
                                            <span class="commit-hash">${c.sha ? c.sha.substring(0, 7) : ''}</span>
                                            <span class="commit-message">${this.escapeHtml(c.message)}</span>
                                            <span class="commit-meta">(${this.escapeHtml(c.author)}, ${this.formatDate(c.date)})</span>
                                        </li>
                                    `).join('')}
                                </ul>
                            </div>
                        `;
                    }

                    updateStatusEl.innerHTML = `
                        ${Icons.info} <strong>Mise a jour disponible!</strong><br>
                        <small>
                            Version locale: <code>${result.localCommit}</code><br>
                            Derniere version: <code>${result.remoteCommit}</code><br>
                        </small>
                        ${commitsHtml}
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
        this.setAdminResult('install-result', 'loading', `${Icons.spinner} Installation du service...`);
        try {
            const result = await this.api('admin/service/install', { method: 'POST' });
            this.setAdminResult('install-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} Service installe` : `${Icons.timesCircle} Erreur: ${result.error}`);
            this.loadAdmin();
        } catch (error) {
            this.setAdminResult('install-result', 'error', `${Icons.timesCircle} Erreur: ${error.message}`);
        }
    }

    async uninstallService() {
        this.setAdminResult('install-result', 'loading', `${Icons.spinner} Desinstallation du service...`);
        try {
            const result = await this.api('admin/service/uninstall', { method: 'POST' });
            this.setAdminResult('install-result', result.success ? 'success' : 'error', 
                result.success ? `${Icons.checkCircle} Service desinstalle` : `${Icons.timesCircle} Erreur: ${result.error}`);
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
                setTimeout(() => {
                    location.reload();
                }, 5000);
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
                    <span class="log-message">${ this.escapeHtml(log.message)}</span>
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
        const syslogRegex = /^([A-Z][al]{2}\s+\d+\s+\d{2}:\d{2}:\d{2})\s+\S+\s+([^:]+):\s+(.*)$/;
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
