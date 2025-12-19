// Network Protocols Management - JavaScript Module

// Extension de FirewallApp pour les protocoles réseau
Object.assign(FirewallApp.prototype, {

    // ==========================================
    // LOAD PROTOCOLS PAGE
    // ==========================================

    async loadProtocols() {
        // Charger les données de l'onglet actif (par défaut IP/ICMP)
        this.loadIpStats();
        this.loadIpInterfaces();
    },

    // ==========================================
    // PROTOCOL TAB NAVIGATION
    // ==========================================

    switchProtocolTab(tabId) {
        // Désactiver tous les onglets
        document.querySelectorAll('.protocol-tab').forEach(tab => {
            tab.classList.toggle('active', tab.dataset.tab === tabId);
        });

        // Afficher le contenu correspondant
        document.querySelectorAll('.protocol-content').forEach(content => {
            content.classList.toggle('active', content.id === `${tabId}-tab`);
        });

        // Charger les données de l'onglet
        switch (tabId) {
            case 'ip-icmp':
                this.loadIpStats();
                this.loadIpInterfaces();
                break;
            case 'routing':
                this.loadRoutingTable();
                this.loadRoutingProtocols();
                break;
            case 'nat':
                this.loadNatConfig();
                this.loadNatRules();
                break;
            case 'ssh':
                this.loadSshConfig();
                this.loadSshKeys();
                this.loadSshSessions();
                break;
            case 'ntp':
                this.loadNtpStatus();
                this.loadNtpServers();
                this.loadTimezones();
                break;
            case 'snmp':
                this.loadSnmpConfig();
                this.loadSnmpTraps();
                break;
        }
    },

    // ==========================================
    // IP / ICMP
    // ==========================================

    async loadIpStats() {
        try {
            const stats = await this.api('networkprotocols/ip/statistics');
            
            document.getElementById('ip-packets-received').textContent = 
                this.formatNumber(stats.packetsReceived || 0);
            document.getElementById('ip-packets-sent').textContent = 
                this.formatNumber(stats.packetsSent || 0);
            document.getElementById('ip-packets-dropped').textContent = 
                this.formatNumber(stats.packetsDropped || 0);
        } catch (error) {
            console.error('Error loading IP stats:', error);
        }
    },

    async loadIpInterfaces() {
        try {
            const interfaces = await this.api('networkprotocols/ip/interfaces');
            const container = document.getElementById('ip-interfaces-list');
            if (!container) return;

            if (!interfaces || !interfaces.length) {
                container.innerHTML = '<p class="empty-state">Aucune interface réseau trouvée</p>';
                return;
            }

            container.innerHTML = interfaces.map(iface => `
                <div class="interface-card">
                    <div class="interface-header">
                        <span class="interface-name">${this.escapeHtml(iface.interfaceName)}</span>
                        <span class="interface-status ${iface.ipv4Enabled ? 'up' : 'down'}">
                            ${iface.ipv4Enabled ? 'Actif' : 'Inactif'}
                        </span>
                    </div>
                    <div class="ip-info">
                        ${iface.ipv4Address ? `
                            <div class="ip-row">
                                <span class="label">IPv4:</span>
                                <span class="value">${this.escapeHtml(iface.ipv4Address)}${iface.ipv4SubnetMask ? '/' + this.subnetToPrefix(iface.ipv4SubnetMask) : ''}</span>
                            </div>
                        ` : ''}
                        ${iface.ipv4Gateway ? `
                            <div class="ip-row">
                                <span class="label">Passerelle:</span>
                                <span class="value">${this.escapeHtml(iface.ipv4Gateway)}</span>
                            </div>
                        ` : ''}
                        ${iface.ipv6Address ? `
                            <div class="ip-row">
                                <span class="label">IPv6:</span>
                                <span class="value">${this.escapeHtml(iface.ipv6Address)}/${iface.ipv6PrefixLength}</span>
                            </div>
                        ` : ''}
                        ${iface.dnsServers && iface.dnsServers.length ? `
                            <div class="ip-row">
                                <span class="label">DNS:</span>
                                <span class="value">${iface.dnsServers.join(', ')}</span>
                            </div>
                        ` : ''}
                        <div class="ip-row">
                            <span class="label">MTU:</span>
                            <span class="value">${iface.mtu}</span>
                        </div>
                        <div class="ip-row">
                            <span class="label">DHCP:</span>
                            <span class="value">${iface.ipv4DHCP ? 'Oui' : 'Non'}</span>
                        </div>
                    </div>
                </div>
            `).join('');
        } catch (error) {
            console.error('Error loading IP interfaces:', error);
        }
    },

    subnetToPrefix(subnet) {
        if (!subnet) return 24;
        const parts = subnet.split('.');
        let bits = 0;
        for (const part of parts) {
            const num = parseInt(part);
            for (let i = 7; i >= 0; i--) {
                if (num & (1 << i)) bits++;
                else return bits;
            }
        }
        return bits;
    },

    formatNumber(num) {
        if (num >= 1000000000) return (num / 1000000000).toFixed(1) + 'G';
        if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
        if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
        return num.toString();
    },

    // ==========================================
    // PING & TRACEROUTE
    // ==========================================

    async runPing() {
        const host = document.getElementById('ping-host')?.value;
        const count = parseInt(document.getElementById('ping-count')?.value) || 4;
        const resultsDiv = document.getElementById('ping-results');

        if (!host) {
            this.showToast({ title: 'Erreur', message: 'Veuillez entrer un hôte', severity: 2 });
            return;
        }

        if (resultsDiv) {
            resultsDiv.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Ping en cours...';
        }

        try {
            const stats = await this.api('networkprotocols/icmp/ping/multiple', {
                method: 'POST',
                body: JSON.stringify({ host, count, timeout: 1000, ttl: 64, bufferSize: 32 })
            });

            let html = `<div class="ping-header">PING ${this.escapeHtml(host)}</div>\n`;
            
            stats.results.forEach((r, i) => {
                if (r.success) {
                    html += `<div class="ping-success">Réponse de ${r.resolvedAddress}: octets=${r.bufferSize} temps=${r.roundtripTime}ms TTL=${r.ttl}</div>\n`;
                } else {
                    html += `<div class="ping-fail">Délai d'attente dépassé${r.errorMessage ? ': ' + r.errorMessage : ''}</div>\n`;
                }
            });

            html += `\n<div class="ping-stats">--- Statistiques ${this.escapeHtml(host)} ---</div>`;
            html += `<div>Paquets: envoyés=${stats.packetsSent}, reçus=${stats.packetsReceived}, perdus=${stats.packetsLost} (${stats.packetLossPercent.toFixed(0)}%)</div>`;
            
            if (stats.packetsReceived > 0) {
                html += `<div>Temps: min=${stats.minRoundtrip}ms, max=${stats.maxRoundtrip}ms, moy=${stats.avgRoundtrip}ms</div>`;
            }

            if (resultsDiv) resultsDiv.innerHTML = html;
        } catch (error) {
            if (resultsDiv) {
                resultsDiv.innerHTML = `<div class="ping-fail">Erreur: ${this.escapeHtml(error.message)}</div>`;
            }
        }
    },

    async runTraceroute() {
        const host = document.getElementById('traceroute-host')?.value;
        const maxHops = parseInt(document.getElementById('traceroute-hops')?.value) || 30;
        const resultsDiv = document.getElementById('traceroute-results');

        if (!host) {
            this.showToast({ title: 'Erreur', message: 'Veuillez entrer un hôte', severity: 2 });
            return;
        }

        if (resultsDiv) {
            resultsDiv.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Traceroute en cours...';
        }

        try {
            const result = await this.api('networkprotocols/icmp/traceroute', {
                method: 'POST',
                body: JSON.stringify({ host, maxHops, timeout: 3000 })
            });

            let html = `<div class="traceroute-header">Traceroute vers ${this.escapeHtml(host)} (${result.resolvedAddress || '?'}), ${result.maxHops} sauts max</div>\n`;

            result.hops.forEach(hop => {
                const times = [hop.roundtripTime1, hop.roundtripTime2, hop.roundtripTime3]
                    .map(t => t !== null ? `${t}ms` : '*').join('  ');
                
                const hostInfo = hop.address ? 
                    (hop.hostname ? `${hop.hostname} (${hop.address})` : hop.address) : 
                    '*';

                html += `<div class="hop-line">${hop.hopNumber.toString().padStart(2)} ${hostInfo.padEnd(40)} ${times}</div>\n`;
            });

            if (result.completed) {
                html += `\n<div class="traceroute-complete">Trace terminée.</div>`;
            }

            if (resultsDiv) resultsDiv.innerHTML = html;
        } catch (error) {
            if (resultsDiv) {
                resultsDiv.innerHTML = `<div class="ping-fail">Erreur: ${this.escapeHtml(error.message)}</div>`;
            }
        }
    },

    // ==========================================
    // ROUTING
    // ==========================================

    async loadRoutingTable() {
        try {
            const routes = await this.api('networkprotocols/routing/table');
            const tbody = document.getElementById('routing-table');
            if (!tbody) return;

            if (!routes || !routes.length) {
                tbody.innerHTML = '<tr><td colspan="6" class="empty-state">Table de routage vide</td></tr>';
                return;
            }

            const protocolNames = { 0: 'Static', 1: 'RIP', 2: 'OSPF', 3: 'BGP' };

            tbody.innerHTML = routes.map(route => `
                <tr>
                    <td>
                        ${this.escapeHtml(route.destination)}/${route.prefixLength}
                        ${route.isDefault ? '<span class="badge">Default</span>' : ''}
                    </td>
                    <td>${this.escapeHtml(route.gateway || 'Direct')}</td>
                    <td>${this.escapeHtml(route.interface)}</td>
                    <td>${route.metric}</td>
                    <td><span class="badge">${protocolNames[route.protocol] || 'Unknown'}</span></td>
                    <td>
                        ${route.protocol === 0 ? `
                            <button class="btn btn-sm btn-danger" onclick="app.deleteRoute(${route.id})" title="Supprimer">
                                <i class="fas fa-trash"></i>
                            </button>
                        ` : '-'}
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('Error loading routing table:', error);
        }
    },

    async loadRoutingProtocols() {
        try {
            const [ripConfig, ospfConfig, bgpConfig] = await Promise.all([
                this.api('networkprotocols/routing/rip/config').catch(() => ({})),
                this.api('networkprotocols/routing/ospf/config').catch(() => ({})),
                this.api('networkprotocols/routing/bgp/config').catch(() => ({}))
            ]);

            // RIP
            document.getElementById('rip-enabled').checked = ripConfig.enabled || false;
            document.getElementById('rip-version').value = ripConfig.version || 2;
            document.getElementById('rip-networks').value = (ripConfig.networks || []).join('\n');

            // OSPF
            document.getElementById('ospf-enabled').checked = ospfConfig.enabled || false;
            document.getElementById('ospf-router-id').value = ospfConfig.routerId || '';
            document.getElementById('ospf-networks').value = (ospfConfig.networks || [])
                .map(n => `${n.network} ${n.wildcard} ${n.areaId}`).join('\n');

            // BGP
            document.getElementById('bgp-enabled').checked = bgpConfig.enabled || false;
            document.getElementById('bgp-local-as').value = bgpConfig.localAS || '';
            document.getElementById('bgp-router-id').value = bgpConfig.routerId || '';
            document.getElementById('bgp-neighbors').value = (bgpConfig.neighbors || [])
                .map(n => `${n.address}:${n.remoteAS}`).join('\n');

        } catch (error) {
            console.error('Error loading routing protocols:', error);
        }
    },

    showAddRouteModal() {
        document.getElementById('modal-title').innerHTML = '<i class="fas fa-route"></i> Ajouter une route';
        document.getElementById('modal-body').innerHTML = `
            <div class="form-group">
                <label>Réseau destination</label>
                <input type="text" id="route-destination" class="form-control" placeholder="192.168.2.0">
            </div>
            <div class="form-group">
                <label>Longueur du préfixe (CIDR)</label>
                <input type="number" id="route-prefix" class="form-control" value="24" min="0" max="32">
            </div>
            <div class="form-group">
                <label>Passerelle</label>
                <input type="text" id="route-gateway" class="form-control" placeholder="192.168.1.1">
            </div>
            <div class="form-group">
                <label>Interface</label>
                <input type="text" id="route-interface" class="form-control" placeholder="eth0">
            </div>
            <div class="form-group">
                <label>Métrique</label>
                <input type="number" id="route-metric" class="form-control" value="1" min="1">
            </div>
        `;
        document.getElementById('modal-footer').innerHTML = `
            <button class="btn btn-secondary" onclick="document.getElementById('modal').classList.remove('active')">Annuler</button>
            <button class="btn btn-primary" onclick="app.addRoute()">
                <i class="fas fa-plus"></i> Ajouter
            </button>
        `;
        document.getElementById('modal').classList.add('active');
    },

    async addRoute() {
        const route = {
            destination: document.getElementById('route-destination')?.value,
            prefixLength: parseInt(document.getElementById('route-prefix')?.value) || 24,
            gateway: document.getElementById('route-gateway')?.value,
            interface: document.getElementById('route-interface')?.value,
            metric: parseInt(document.getElementById('route-metric')?.value) || 1
        };

        try {
            await this.api('networkprotocols/routing/routes', {
                method: 'POST',
                body: JSON.stringify(route)
            });
            document.getElementById('modal').classList.remove('active');
            this.showToast({ title: 'Succès', message: 'Route ajoutée', severity: 0 });
            this.loadRoutingTable();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async deleteRoute(id) {
        if (!confirm('Supprimer cette route ?')) return;

        try {
            await this.api(`networkprotocols/routing/routes/${id}`, { method: 'DELETE' });
            this.showToast({ title: 'Succès', message: 'Route supprimée', severity: 0 });
            this.loadRoutingTable();
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async toggleRip() {
        await this.saveRipConfig();
    },

    async saveRipConfig() {
        const config = {
            enabled: document.getElementById('rip-enabled')?.checked || false,
            version: parseInt(document.getElementById('rip-version')?.value) || 2,
            networks: (document.getElementById('rip-networks')?.value || '')
                .split('\n').filter(n => n.trim()),
            updateInterval: 30,
            timeoutInterval: 180,
            garbageCollectionInterval: 120,
            splitHorizon: true,
            poisonReverse: true
        };

        try {
            await this.api('networkprotocols/routing/rip/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration RIP enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async toggleOspf() {
        await this.saveOspfConfig();
    },

    async saveOspfConfig() {
        const networksText = document.getElementById('ospf-networks')?.value || '';
        const networks = networksText.split('\n').filter(n => n.trim()).map(line => {
            const parts = line.trim().split(/\s+/);
            return {
                network: parts[0] || '',
                wildcard: parts[1] || '0.0.0.255',
                areaId: parseInt(parts[2]) || 0
            };
        });

        const config = {
            enabled: document.getElementById('ospf-enabled')?.checked || false,
            routerId: document.getElementById('ospf-router-id')?.value || '',
            processId: 1,
            networks: networks,
            helloInterval: 10,
            deadInterval: 40,
            autoCost: true,
            referenceBandwidth: 100
        };

        try {
            await this.api('networkprotocols/routing/ospf/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration OSPF enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    },

    async toggleBgp() {
        await this.saveBgpConfig();
    },

    async saveBgpConfig() {
        const neighborsText = document.getElementById('bgp-neighbors')?.value || '';
        const neighbors = neighborsText.split('\n').filter(n => n.trim()).map(line => {
            const [address, as] = line.trim().split(':');
            return {
                address: address || '',
                remoteAS: parseInt(as) || 0,
                enabled: true
            };
        });

        const config = {
            enabled: document.getElementById('bgp-enabled')?.checked || false,
            localAS: parseInt(document.getElementById('bgp-local-as')?.value) || 0,
            routerId: document.getElementById('bgp-router-id')?.value || '',
            neighbors: neighbors,
            keepaliveInterval: 60,
            holdTime: 180
        };

        try {
            await this.api('networkprotocols/routing/bgp/config', {
                method: 'PUT',
                body: JSON.stringify(config)
            });
            this.showToast({ title: 'Succès', message: 'Configuration BGP enregistrée', severity: 0 });
        } catch (error) {
            this.showToast({ title: 'Erreur', message: error.message, severity: 2 });
        }
    }
});
