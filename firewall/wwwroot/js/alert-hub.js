// AlertHub SignalR Client pour les logs et alertes en temps réel

class AlertHubClient {
    constructor(app) {
        this.app = app;
        this.connection = null;
        this.isConnected = false;
        this.securityLogs = [];
        this.maxLogs = 100;
    }

    async connect() {
        if (typeof signalR === 'undefined') {
            console.warn('SignalR not loaded, alerts real-time updates disabled');
            return;
        }

        try {
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/alerts')
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            this.setupHandlers();

            this.connection.onclose(() => {
                console.log('AlertHub déconnecté');
                this.isConnected = false;
            });

            this.connection.onreconnecting(() => {
                console.log('AlertHub reconnexion en cours...');
                this.isConnected = false;
            });

            this.connection.onreconnected(() => {
                console.log('AlertHub reconnecté');
                this.isConnected = true;
            });

            await this.connection.start();
            this.isConnected = true;
            console.log('Connecté à l\'AlertHub');
        } catch (error) {
            console.error('Erreur connexion AlertHub:', error);
            setTimeout(() => this.connect(), 5000);
        }
    }

    setupHandlers() {
        // Nouvelle alerte réseau
        this.connection.on('NewAlert', (alert) => {
            console.log('Nouvelle alerte:', alert);
            this.handleNewAlert(alert);
        });

        // Nouveau log de sécurité
        this.connection.on('NewSecurityLog', (log) => {
            console.log('Nouveau log sécurité:', log);
            this.handleNewSecurityLog(log);
        });

        // Événement de blocage
        this.connection.on('BlockEvent', (event) => {
            console.log('Événement de blocage:', event);
            this.handleBlockEvent(event);
        });

        // Alerte lue
        this.connection.on('AlertRead', (alertId) => {
            this.handleAlertRead(alertId);
        });

        // Alerte résolue
        this.connection.on('AlertResolved', (alertId) => {
            this.handleAlertResolved(alertId);
        });

        // Toutes alertes lues
        this.connection.on('AllAlertsRead', () => {
            this.handleAllAlertsRead();
        });

        // Toutes alertes résolues
        this.connection.on('AllAlertsResolved', () => {
            this.handleAllAlertsResolved();
        });

        // Stats mises à jour
        this.connection.on('StatsUpdate', (stats) => {
            this.handleStatsUpdate(stats);
        });

        // Reset des logs
        this.connection.on('LogsReset', () => {
            this.handleLogsReset();
        });
    }

    handleNewAlert(alert) {
        // Mettre à jour le badge
        this.updateAlertBadge(1);

        // Afficher une notification toast
        this.app.showToast({
            title: alert.title || 'Nouvelle alerte',
            message: alert.message,
            severity: this.getSeverityValue(alert.severity)
        });

        // Si on est sur la page alerts, mettre à jour la table
        if (this.app.currentPage === 'alerts') {
            this.addAlertToTable(alert);
        }

        // Mettre à jour le dashboard si actif
        if (this.app.currentPage === 'dashboard') {
            this.app.loadDashboard();
        }
    }

    handleNewSecurityLog(log) {
        // Ajouter au cache local
        this.securityLogs.unshift(log);
        if (this.securityLogs.length > this.maxLogs) {
            this.securityLogs.pop();
        }

        // Afficher une notification pour les logs Critical ou Warning
        if (log.severity === 'Critical' || log.severity === 'Warning') {
            this.app.showToast({
                title: log.actionTaken,
                message: log.message,
                severity: log.severity === 'Critical' ? 3 : 2
            });
        }

        // Mettre à jour la table des logs si visible
        this.updateLogsTable(log);

        // Mettre à jour le compteur de logs non lus
        this.updateLogsUnreadCount();
    }

    handleBlockEvent(event) {
        // Log dans la console pour le monitoring
        console.log(`[BLOCK] ${event.deviceName || event.sourceMac} -> ${event.destinationIp}:${event.destinationPort}`);

        // Notification visuelle
        this.app.showToast({
            title: 'Trafic bloqué',
            message: `${event.deviceName || event.sourceMac} a tenté de joindre ${event.destinationIp}`,
            severity: 2
        });

        // Mettre à jour l'interface si on est sur la page logs
        if (this.app.currentPage === 'traffic' || this.app.currentPage === 'alerts') {
            this.addBlockEventToDisplay(event);
        }
    }

    handleAlertRead(alertId) {
        const row = document.querySelector(`tr[data-alert-id="${alertId}"]`);
        if (row) {
            row.classList.remove('unread');
            row.classList.add('read');
        }
        this.updateAlertBadge(-1);
    }

    handleAlertResolved(alertId) {
        const row = document.querySelector(`tr[data-alert-id="${alertId}"]`);
        if (row) {
            row.classList.add('resolved');
            // Optionnel: animation de suppression
            row.style.opacity = '0.5';
        }
    }

    handleAllAlertsRead() {
        document.querySelectorAll('.alerts-table tr.unread').forEach(row => {
            row.classList.remove('unread');
            row.classList.add('read');
        });
        this.setAlertBadge(0);
    }

    handleAllAlertsResolved() {
        const tbody = document.getElementById('alerts-table');
        if (tbody) {
            tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Toutes les alertes ont été résolues</td></tr>';
        }
        this.setAlertBadge(0);
    }

    handleStatsUpdate(stats) {
        // Mettre à jour les widgets de statistiques
        const criticalEl = document.getElementById('critical-alerts-count');
        if (criticalEl && stats.BySeverity) {
            criticalEl.textContent = stats.BySeverity['Critical'] || 0;
        }

        const totalEl = document.getElementById('total-alerts-count');
        if (totalEl) {
            totalEl.textContent = stats.Total || 0;
        }
    }

    handleLogsReset() {
        this.securityLogs = [];
        
        // Vider les tables
        const logsTable = document.getElementById('security-logs-table');
        if (logsTable) {
            logsTable.innerHTML = '<tr><td colspan="6" class="empty-state">Logs réinitialisés</td></tr>';
        }

        this.app.showToast({
            title: 'Réinitialisation',
            message: 'Les logs ont été réinitialisés',
            severity: 0
        });
    }

    // Méthodes utilitaires
    addAlertToTable(alert) {
        const tbody = document.getElementById('alerts-table');
        if (!tbody) return;

        // Supprimer le message "aucune alerte" si présent
        const emptyRow = tbody.querySelector('.empty-state');
        if (emptyRow) {
            emptyRow.closest('tr').remove();
        }

        const row = document.createElement('tr');
        row.dataset.alertId = alert.id;
        row.className = 'unread row-new';
        row.innerHTML = `
            <td>${this.getSeverityBadge(alert.severity)}</td>
            <td>${this.escapeHtml(alert.title || alert.type)}</td>
            <td>${this.escapeHtml(alert.message)}</td>
            <td>${this.escapeHtml(alert.sourceIp || alert.sourceMac || '-')}</td>
            <td>${this.formatDate(alert.timestamp)}</td>
            <td>
                <button class="btn btn-sm btn-primary" onclick="app.alertHub.markAlertRead(${alert.id})">
                    <i class="fas fa-check"></i>
                </button>
                <button class="btn btn-sm btn-success" onclick="app.alertHub.resolveAlert(${alert.id})">
                    <i class="fas fa-check-double"></i>
                </button>
            </td>
        `;

        tbody.insertBefore(row, tbody.firstChild);

        // Animation
        setTimeout(() => row.classList.remove('row-new'), 2000);
    }

    updateLogsTable(log) {
        const tbody = document.getElementById('security-logs-table');
        if (!tbody) return;

        // Supprimer le message vide si présent
        const emptyRow = tbody.querySelector('.empty-state');
        if (emptyRow) {
            emptyRow.closest('tr').remove();
        }

        const row = document.createElement('tr');
        row.dataset.logId = log.id;
        row.className = `log-${log.severity.toLowerCase()} row-new`;
        row.innerHTML = `
            <td>${this.formatTime(log.timestamp)}</td>
            <td>${this.getSeverityBadge(log.severity)}</td>
            <td><span class="category-badge">${this.escapeHtml(log.category)}</span></td>
            <td>${this.escapeHtml(log.actionTaken)}</td>
            <td>${this.escapeHtml(log.message)}</td>
            <td>${this.escapeHtml(log.sourceIp || log.sourceMac || '-')}</td>
        `;

        tbody.insertBefore(row, tbody.firstChild);

        // Limiter le nombre de lignes affichées
        const rows = tbody.querySelectorAll('tr');
        if (rows.length > this.maxLogs) {
            rows[rows.length - 1].remove();
        }

        // Animation
        setTimeout(() => row.classList.remove('row-new'), 2000);
    }

    addBlockEventToDisplay(event) {
        // Ajouter à la liste des événements de blocage en temps réel
        const container = document.getElementById('block-events-live');
        if (!container) return;

        const eventEl = document.createElement('div');
        eventEl.className = 'block-event-item';
        eventEl.innerHTML = `
            <span class="event-time">${this.formatTime(event.timestamp)}</span>
            <span class="event-icon"><i class="fas fa-ban"></i></span>
            <span class="event-source">${this.escapeHtml(event.deviceName || event.sourceMac)}</span>
            <span class="event-arrow">?</span>
            <span class="event-dest">${this.escapeHtml(event.destinationIp)}:${event.destinationPort || '?'}</span>
            <span class="event-count">(${event.packetCount} paquets)</span>
        `;

        container.insertBefore(eventEl, container.firstChild);

        // Limiter le nombre d'événements affichés
        const events = container.querySelectorAll('.block-event-item');
        if (events.length > 50) {
            events[events.length - 1].remove();
        }
    }

    // Actions
    async markAlertRead(alertId) {
        try {
            await fetch(`/api/alerts/${alertId}/read`, { method: 'POST' });
        } catch (error) {
            console.error('Erreur markAlertRead:', error);
        }
    }

    async resolveAlert(alertId) {
        try {
            await fetch(`/api/alerts/${alertId}/resolve`, { method: 'POST' });
        } catch (error) {
            console.error('Erreur resolveAlert:', error);
        }
    }

    async markAllRead() {
        try {
            await fetch('/api/alerts/read-all', { method: 'POST' });
            this.app.showToast({
                title: 'Succès',
                message: 'Toutes les alertes ont été marquées comme lues',
                severity: 0
            });
        } catch (error) {
            console.error('Erreur markAllRead:', error);
        }
    }

    async resolveAll() {
        try {
            await fetch('/api/alerts/resolve-all', { method: 'POST' });
            this.app.showToast({
                title: 'Succès',
                message: 'Toutes les alertes ont été résolues',
                severity: 0
            });
        } catch (error) {
            console.error('Erreur resolveAll:', error);
        }
    }

    async resetLogs() {
        if (!confirm('Êtes-vous sûr de vouloir réinitialiser tous les logs ?')) return;
        
        try {
            await fetch('/api/logs/reset', { method: 'POST' });
        } catch (error) {
            console.error('Erreur resetLogs:', error);
        }
    }

    // Utilitaires
    updateAlertBadge(delta) {
        const badge = document.getElementById('alert-badge');
        if (badge) {
            let count = parseInt(badge.textContent || '0') + delta;
            if (count < 0) count = 0;
            badge.textContent = count;
            badge.style.display = count > 0 ? 'inline' : 'none';
        }
    }

    setAlertBadge(count) {
        const badge = document.getElementById('alert-badge');
        if (badge) {
            badge.textContent = count;
            badge.style.display = count > 0 ? 'inline' : 'none';
        }
    }

    updateLogsUnreadCount() {
        const countEl = document.getElementById('logs-unread-count');
        if (countEl) {
            let count = parseInt(countEl.textContent || '0') + 1;
            countEl.textContent = count;
        }
    }

    getSeverityBadge(severity) {
        const severityClass = {
            'Info': 'severity-info',
            'Warning': 'severity-warning',
            'Critical': 'severity-critical',
            'Low': 'severity-low',
            'Medium': 'severity-medium',
            'High': 'severity-high'
        };
        const className = severityClass[severity] || 'severity-info';
        return `<span class="severity-badge ${className}">${severity}</span>`;
    }

    getSeverityValue(severity) {
        const values = { 'Info': 0, 'Low': 1, 'Medium': 2, 'Warning': 2, 'High': 3, 'Critical': 3 };
        return values[severity] || 0;
    }

    formatDate(dateStr) {
        if (!dateStr) return '-';
        const date = new Date(dateStr);
        return date.toLocaleString('fr-FR');
    }

    formatTime(dateStr) {
        if (!dateStr) return '-';
        const date = new Date(dateStr);
        return date.toLocaleTimeString('fr-FR');
    }

    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    disconnect() {
        if (this.connection) {
            this.connection.stop().catch(err => console.error('Error stopping AlertHub:', err));
        }
    }
}

// Exporter pour utilisation
window.AlertHubClient = AlertHubClient;
