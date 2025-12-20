// Compliance Module JavaScript
const API_BASE = '/api/compliance';

// État global
let currentTab = 'dashboard';
let iso27001Controls = [];
let iso15408Data = {};
let editingRiskId = null;
let realComplianceResults = null;

// Initialisation
document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    loadDashboard();
});

// Gestion des onglets
function initTabs() {
    document.querySelectorAll('.tab-compliance').forEach(tab => {
        tab.addEventListener('click', () => {
            document.querySelectorAll('.tab-compliance').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
            
            tab.classList.add('active');
            const tabId = tab.dataset.tab;
            document.getElementById(tabId).classList.add('active');
            currentTab = tabId;
            
            // Charger les données de l'onglet
            switch(tabId) {
                case 'dashboard': loadDashboard(); break;
                case 'iso27001': loadIso27001Controls(); break;
                case 'iso15408': loadIso15408Data(); break;
                case 'risks': loadRisks(); break;
                case 'incidents': loadIncidents(); break;
                case 'audits': loadAudits(); break;
            }
        });
    });
}

// Dashboard
async function loadDashboard() {
    try {
        // Charger le dashboard ET les vérifications automatiques en parallèle
        const [data, realChecks] = await Promise.all([
            fetch(`${API_BASE}/dashboard`).then(r => r.json()),
            fetch(`${API_BASE}/checks`).then(r => r.json()).catch(() => null)
        ]);
        
        realComplianceResults = realChecks;
        
        // ISO 27001 Score - utiliser le score réel si disponible
        const realScore = realChecks ? realChecks.compliancePercentage : data.iso27001.compliancePercentage;
        updateProgressRing('iso27001-ring', realScore);
        const score27001 = document.getElementById('iso27001-score');
        score27001.textContent = `${realScore.toFixed(1)}%`;
        score27001.className = `score-value ${getScoreClass(realScore)}`;
        
        // ISO 15408 Score
        const avgScore = (data.iso15408.functionalCompliancePercentage + data.iso15408.assuranceCompliancePercentage) / 2;
        updateProgressRing('iso15408-ring', avgScore);
        const score15408 = document.getElementById('iso15408-score');
        score15408.textContent = `${avgScore.toFixed(1)}%`;
        score15408.className = `score-value ${getScoreClass(avgScore)}`;
        document.getElementById('eal-badge').textContent = `EAL${data.iso15408.targetEal}`;
        
        // Afficher les vérifications automatiques
        if (realChecks) {
            renderRealComplianceChecks(realChecks);
        }
        
        // Tâches prioritaires
        const tasksList = document.getElementById('upcoming-tasks');
        if (data.upcomingTasks.length > 0) {
            tasksList.innerHTML = data.upcomingTasks.map(task => `
                <li class="task-item">
                    <span class="task-priority ${task.priority.toLowerCase()}"></span>
                    <div>
                        <strong>${task.taskType}</strong><br>
                        <small>${task.description}</small><br>
                        <small class="text-muted">Échéance: ${formatDate(task.dueDate)}</small>
                    </div>
                </li>
            `).join('');
        } else {
            tasksList.innerHTML = '<li class="task-item">Aucune tâche prioritaire</li>';
        }
        
        // Risques majeurs
        const risksDiv = document.getElementById('top-risks');
        if (data.topRisks.length > 0) {
            risksDiv.innerHTML = data.topRisks.map(risk => `
                <div class="finding-card ${risk.riskLevel.toLowerCase()}">
                    <strong>${risk.assetName}</strong>
                    <p>${risk.threatDescription}</p>
                    <small>Score: ${risk.riskScore} | ${risk.status}</small>
                </div>
            `).join('');
        } else {
            risksDiv.innerHTML = '<p>Aucun risque majeur identifié</p>';
        }
        
        // Constatations ouvertes
        const findingsDiv = document.getElementById('open-findings');
        if (data.openFindings.length > 0) {
            findingsDiv.innerHTML = data.openFindings.map(finding => `
                <div class="finding-card ${finding.severity.toLowerCase()}">
                    <strong>[${finding.controlId}] ${getSeverityLabel(finding.severity)}</strong>
                    <p>${finding.description}</p>
                    ${finding.recommendation ? `<small><em>Recommandation: ${finding.recommendation}</em></small>` : ''}

                </div>
            `).join('');
        } else {
            findingsDiv.innerHTML = '<p class="success">? Aucune constatation ouverte</p>';
        }
        
    } catch (error) {
        console.error('Erreur chargement dashboard:', error);
    }
}

// Afficher les vérifications de conformité réelles
function renderRealComplianceChecks(data) {
    const container = document.getElementById('real-compliance-checks');
    if (!container) return;
    
    const statusIcons = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    
    const statusColors = {
        'Compliant': 'var(--success-color)',
        'PartiallyCompliant': 'var(--warning-color)',
        'NonCompliant': 'var(--danger-color)',
        'NotVerifiable': 'var(--text-secondary)',
        'Error': 'var(--danger-color)'
    };
    
    // Grouper par statut
    const byStatus = {
        'NonCompliant': [],
        'PartiallyCompliant': [],
        'Compliant': [],
        'NotVerifiable': [],
        'Error': []
    };
    
    data.checkResults.forEach(r => {
        if (byStatus[r.status]) {
            byStatus[r.status].push(r);
        }
    });
    
    let html = `
        <div class="compliance-summary-bar">
            <div class="summary-stat compliant">
                <span class="stat-number">${data.compliant}</span>
                <span class="stat-label">Conforme</span>
            </div>
            <div class="summary-stat partial">
                <span class="stat-number">${data.partiallyCompliant}</span>
                <span class="stat-label">Partiel</span>
            </div>
            <div class="summary-stat non-compliant">
                <span class="stat-number">${data.nonCompliant}</span>
                <span class="stat-label">Non conforme</span>
            </div>
            <div class="summary-stat not-verifiable">
                <span class="stat-number">${data.notVerifiable}</span>
                <span class="stat-label">N/A</span>
            </div>
        </div>
        <div class="compliance-checks-grid">
    `;
    
    // Afficher d'abord les non-conformes, puis les partiels, puis les conformes
    [...byStatus['NonCompliant'], ...byStatus['PartiallyCompliant'], ...byStatus['Compliant'], ...byStatus['NotVerifiable']].forEach(check => {
        html += `
            <div class="compliance-check-card ${check.status.toLowerCase()}" onclick="showCheckDetails('${check.controlId}')">
                <div class="check-header">
                    <span class="check-icon">${statusIcons[check.status] || '?'}</span>
                    <span class="check-id">${check.controlId}</span>
                </div>
                <div class="check-title">${check.controlTitle}</div>
                <div class="check-message">${check.message}</div>
                ${check.recommendation ? `<div class="check-recommendation"><strong>?</strong> ${check.recommendation}</div>` : ''}
            </div>
        `;
    });
    
    html += '</div>';
    
    // Ajouter la date de vérification
    html += `<div class="check-timestamp">Dernière vérification: ${formatDate(data.checkedAt)}</div>`;
    
    container.innerHTML = html;
}

// Afficher les détails d'un contrôle
async function showCheckDetails(controlId) {
    try {
        const result = await fetch(`${API_BASE}/checks/${controlId}`).then(r => r.json());
        
        let detailsHtml = '<div class="check-details-grid">';
        
        for (const [key, value] of Object.entries(result.details || {})) {
            detailsHtml += `
                <div class="detail-item">
                    <span class="detail-key">${formatDetailKey(key)}</span>
                    <span class="detail-value">${formatDetailValue(value)}</span>
                </div>
            `;
        }
        
        detailsHtml += '</div>';
        
        // Créer le modal
        const existingModal = document.getElementById('check-details-modal');
        if (existingModal) existingModal.remove();
        
        const modal = document.createElement('div');
        modal.className = 'modal';
        modal.id = 'check-details-modal';
        modal.style.display = 'block';
        modal.innerHTML = `
            <div class="modal-content">
                <span class="close" onclick="document.getElementById('check-details-modal').remove();">&times;</span>
                <h2>${getStatusEmoji(result.status)} ${result.controlId} - ${result.controlTitle}</h2>
                <div class="check-status-badge ${result.status.toLowerCase()}">${getStatusLabel(result.status)}</div>
                <p class="check-detail-message">${result.message}</p>
                ${result.recommendation ? `<div class="recommendation-box"><strong>Recommandation:</strong> ${result.recommendation}</div>` : ''}
                <h3>Détails de la vérification</h3>
                ${detailsHtml}
                <div class="check-time">Vérifié le: ${formatDate(result.checkedAt)}</div>
            </div>
        `;
        
        document.body.appendChild(modal);
        
        modal.onclick = function(e) {
            if (e.target === modal) modal.remove();
        };
    } catch (error) {
        console.error('Erreur chargement détails:', error);
    }
}

function formatDetailKey(key) {
    return key
        .replace(/([A-Z])/g, ' $1')
        .replace(/^./, str => str.toUpperCase())
        .trim();
}

function formatDetailValue(value) {
    if (typeof value === 'boolean') {
        return value ? '? Oui' : '? Non';
    }
    if (Array.isArray(value)) {
        return value.join(', ') || '-';
    }
    if (typeof value === 'object') {
        return JSON.stringify(value, null, 2);
    }
    return value ?? '-';
}

function getStatusEmoji(status) {
    const emojis = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    return emojis[status] || '?';
}

function getStatusLabel(status) {
    const labels = {
        'Compliant': 'Conforme',
        'PartiallyCompliant': 'Partiellement conforme',
        'NonCompliant': 'Non conforme',
        'NotVerifiable': 'Non vérifiable',
        'Error': 'Erreur'
    };
    return labels[status] || status;
}

// Forcer la re-vérification
async function runComplianceChecks() {
    const container = document.getElementById('real-compliance-checks');
    if (container) {
        container.innerHTML = '<div class="loading"><i class="fas fa-spinner fa-spin"></i> Vérification en cours...</div>';
    }
    
    try {
        const results = await fetch(`${API_BASE}/checks/run`, { method: 'POST' }).then(r => r.json());
        realComplianceResults = { checkResults: results };
        
        // Recalculer le résumé
        const summary = {
            totalChecks: results.length,
            compliant: results.filter(r => r.status === 'Compliant').length,
            partiallyCompliant: results.filter(r => r.status === 'PartiallyCompliant').length,
            nonCompliant: results.filter(r => r.status === 'NonCompliant').length,
            notVerifiable: results.filter(r => r.status === 'NotVerifiable').length,
            errors: results.filter(r => r.status === 'Error').length,
            checkResults: results,
            checkedAt: new Date().toISOString()
        };
        summary.compliancePercentage = summary.totalChecks > 0 ?
            ((summary.compliant + summary.partiallyCompliant * 0.5) / summary.totalChecks * 100) : 0;
        
        renderRealComplianceChecks(summary);
        showNotification('Vérification terminée', 'success');
    } catch (error) {
        console.error('Erreur vérification:', error);
        if (container) {
            container.innerHTML = '<div class="error">Erreur lors de la vérification</div>';
        }
        showNotification('Erreur lors de la vérification', 'error');
    }
}

// Dashboard
async function loadDashboard() {
    try {
        // Charger le dashboard ET les vérifications automatiques en parallèle
        const [data, realChecks] = await Promise.all([
            fetch(`${API_BASE}/dashboard`).then(r => r.json()),
            fetch(`${API_BASE}/checks`).then(r => r.json()).catch(() => null)
        ]);
        
        realComplianceResults = realChecks;
        
        // ISO 27001 Score - utiliser le score réel si disponible
        const realScore = realChecks ? realChecks.compliancePercentage : data.iso27001.compliancePercentage;
        updateProgressRing('iso27001-ring', realScore);
        const score27001 = document.getElementById('iso27001-score');
        score27001.textContent = `${realScore.toFixed(1)}%`;
        score27001.className = `score-value ${getScoreClass(realScore)}`;
        
        // ISO 15408 Score
        const avgScore = (data.iso15408.functionalCompliancePercentage + data.iso15408.assuranceCompliancePercentage) / 2;
        updateProgressRing('iso15408-ring', avgScore);
        const score15408 = document.getElementById('iso15408-score');
        score15408.textContent = `${avgScore.toFixed(1)}%`;
        score15408.className = `score-value ${getScoreClass(avgScore)}`;
        document.getElementById('eal-badge').textContent = `EAL${data.iso15408.targetEal}`;
        
        // Afficher les vérifications automatiques
        if (realChecks) {
            renderRealComplianceChecks(realChecks);
        }
        
        // Tâches prioritaires
        const tasksList = document.getElementById('upcoming-tasks');
        if (data.upcomingTasks.length > 0) {
            tasksList.innerHTML = data.upcomingTasks.map(task => `
                <li class="task-item">
                    <span class="task-priority ${task.priority.toLowerCase()}"></span>
                    <div>
                        <strong>${task.taskType}</strong><br>
                        <small>${task.description}</small><br>
                        <small class="text-muted">Échéance: ${formatDate(task.dueDate)}</small>
                    </div>
                </li>
            `).join('');
        } else {
            tasksList.innerHTML = '<li class="task-item">Aucune tâche prioritaire</li>';
        }
        
        // Risques majeurs
        const risksDiv = document.getElementById('top-risks');
        if (data.topRisks.length > 0) {
            risksDiv.innerHTML = data.topRisks.map(risk => `
                <div class="finding-card ${risk.riskLevel.toLowerCase()}">
                    <strong>${risk.assetName}</strong>
                    <p>${risk.threatDescription}</p>
                    <small>Score: ${risk.riskScore} | ${risk.status}</small>
                </div>
            `).join('');
        } else {
            risksDiv.innerHTML = '<p>Aucun risque majeur identifié</p>';
        }
        
        // Constatations ouvertes
        const findingsDiv = document.getElementById('open-findings');
        if (data.openFindings.length > 0) {
            findingsDiv.innerHTML = data.openFindings.map(finding => `
                <div class="finding-card ${finding.severity.toLowerCase()}">
                    <strong>[${finding.controlId}] ${getSeverityLabel(finding.severity)}</strong>
                    <p>${finding.description}</p>
                    ${finding.recommendation ? `<small><em>Recommandation: ${finding.recommendation}</em></small>` : ''}

                </div>
            `).join('');
        } else {
            findingsDiv.innerHTML = '<p class="success">? Aucune constatation ouverte</p>';
        }
        
    } catch (error) {
        console.error('Erreur chargement dashboard:', error);
    }
}

// Afficher les vérifications de conformité réelles
function renderRealComplianceChecks(data) {
    const container = document.getElementById('real-compliance-checks');
    if (!container) return;
    
    const statusIcons = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    
    const statusColors = {
        'Compliant': 'var(--success-color)',
        'PartiallyCompliant': 'var(--warning-color)',
        'NonCompliant': 'var(--danger-color)',
        'NotVerifiable': 'var(--text-secondary)',
        'Error': 'var(--danger-color)'
    };
    
    // Grouper par statut
    const byStatus = {
        'NonCompliant': [],
        'PartiallyCompliant': [],
        'Compliant': [],
        'NotVerifiable': [],
        'Error': []
    };
    
    data.checkResults.forEach(r => {
        if (byStatus[r.status]) {
            byStatus[r.status].push(r);
        }
    });
    
    let html = `
        <div class="compliance-summary-bar">
            <div class="summary-stat compliant">
                <span class="stat-number">${data.compliant}</span>
                <span class="stat-label">Conforme</span>
            </div>
            <div class="summary-stat partial">
                <span class="stat-number">${data.partiallyCompliant}</span>
                <span class="stat-label">Partiel</span>
            </div>
            <div class="summary-stat non-compliant">
                <span class="stat-number">${data.nonCompliant}</span>
                <span class="stat-label">Non conforme</span>
            </div>
            <div class="summary-stat not-verifiable">
                <span class="stat-number">${data.notVerifiable}</span>
                <span class="stat-label">N/A</span>
            </div>
        </div>
        <div class="compliance-checks-grid">
    `;
    
    // Afficher d'abord les non-conformes, puis les partiels, puis les conformes
    [...byStatus['NonCompliant'], ...byStatus['PartiallyCompliant'], ...byStatus['Compliant'], ...byStatus['NotVerifiable']].forEach(check => {
        html += `
            <div class="compliance-check-card ${check.status.toLowerCase()}" onclick="showCheckDetails('${check.controlId}')">
                <div class="check-header">
                    <span class="check-icon">${statusIcons[check.status] || '?'}</span>
                    <span class="check-id">${check.controlId}</span>
                </div>
                <div class="check-title">${check.controlTitle}</div>
                <div class="check-message">${check.message}</div>
                ${check.recommendation ? `<div class="check-recommendation"><strong>?</strong> ${check.recommendation}</div>` : ''}
            </div>
        `;
    });
    
    html += '</div>';
    
    // Ajouter la date de vérification
    html += `<div class="check-timestamp">Dernière vérification: ${formatDate(data.checkedAt)}</div>`;
    
    container.innerHTML = html;
}

// Afficher les détails d'un contrôle
async function showCheckDetails(controlId) {
    try {
        const result = await fetch(`${API_BASE}/checks/${controlId}`).then(r => r.json());
        
        let detailsHtml = '<div class="check-details-grid">';
        
        for (const [key, value] of Object.entries(result.details || {})) {
            detailsHtml += `
                <div class="detail-item">
                    <span class="detail-key">${formatDetailKey(key)}</span>
                    <span class="detail-value">${formatDetailValue(value)}</span>
                </div>
            `;
        }
        
        detailsHtml += '</div>';
        
        // Créer le modal
        const existingModal = document.getElementById('check-details-modal');
        if (existingModal) existingModal.remove();
        
        const modal = document.createElement('div');
        modal.className = 'modal';
        modal.id = 'check-details-modal';
        modal.style.display = 'block';
        modal.innerHTML = `
            <div class="modal-content">
                <span class="close" onclick="document.getElementById('check-details-modal').remove();">&times;</span>
                <h2>${getStatusEmoji(result.status)} ${result.controlId} - ${result.controlTitle}</h2>
                <div class="check-status-badge ${result.status.toLowerCase()}">${getStatusLabel(result.status)}</div>
                <p class="check-detail-message">${result.message}</p>
                ${result.recommendation ? `<div class="recommendation-box"><strong>Recommandation:</strong> ${result.recommendation}</div>` : ''}
                <h3>Détails de la vérification</h3>
                ${detailsHtml}
                <div class="check-time">Vérifié le: ${formatDate(result.checkedAt)}</div>
            </div>
        `;
        
        document.body.appendChild(modal);
        
        modal.onclick = function(e) {
            if (e.target === modal) modal.remove();
        };
    } catch (error) {
        console.error('Erreur chargement détails:', error);
    }
}

function formatDetailKey(key) {
    return key
        .replace(/([A-Z])/g, ' $1')
        .replace(/^./, str => str.toUpperCase())
        .trim();
}

function formatDetailValue(value) {
    if (typeof value === 'boolean') {
        return value ? '? Oui' : '? Non';
    }
    if (Array.isArray(value)) {
        return value.join(', ') || '-';
    }
    if (typeof value === 'object') {
        return JSON.stringify(value, null, 2);
    }
    return value ?? '-';
}

function getStatusEmoji(status) {
    const emojis = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    return emojis[status] || '?';
}

function getStatusLabel(status) {
    const labels = {
        'Compliant': 'Conforme',
        'PartiallyCompliant': 'Partiellement conforme',
        'NonCompliant': 'Non conforme',
        'NotVerifiable': 'Non vérifiable',
        'Error': 'Erreur'
    };
    return labels[status] || status;
}

// Forcer la re-vérification
async function runComplianceChecks() {
    const container = document.getElementById('real-compliance-checks');
    if (container) {
        container.innerHTML = '<div class="loading"><i class="fas fa-spinner fa-spin"></i> Vérification en cours...</div>';
    }
    
    try {
        const results = await fetch(`${API_BASE}/checks/run`, { method: 'POST' }).then(r => r.json());
        realComplianceResults = { checkResults: results };
        
        // Recalculer le résumé
        const summary = {
            totalChecks: results.length,
            compliant: results.filter(r => r.status === 'Compliant').length,
            partiallyCompliant: results.filter(r => r.status === 'PartiallyCompliant').length,
            nonCompliant: results.filter(r => r.status === 'NonCompliant').length,
            notVerifiable: results.filter(r => r.status === 'NotVerifiable').length,
            errors: results.filter(r => r.status === 'Error').length,
            checkResults: results,
            checkedAt: new Date().toISOString()
        };
        summary.compliancePercentage = summary.totalChecks > 0 ?
            ((summary.compliant + summary.partiallyCompliant * 0.5) / summary.totalChecks * 100) : 0;
        
        renderRealComplianceChecks(summary);
        showNotification('Vérification terminée', 'success');
    } catch (error) {
        console.error('Erreur vérification:', error);
        if (container) {
            container.innerHTML = '<div class="error">Erreur lors de la vérification</div>';
        }
        showNotification('Erreur lors de la vérification', 'error');
    }
}

// …existing code for updateProgressRing, getScoreClass, loadIso27001Controls, etc...

function updateProgressRing(id, percentage) {
    const circle = document.getElementById(id);
    if (!circle) return;
    
    const circumference = 2 * Math.PI * 65;
    circle.style.strokeDasharray = circumference;
    circle.style.strokeDashoffset = circumference - (percentage / 100) * circumference;
    
    // Couleur selon le score
    if (percentage >= 80) {
        circle.style.stroke = 'var(--success-color)';
    } else if (percentage >= 50) {
        circle.style.stroke = 'var(--warning-color)';
    } else {
        circle.style.stroke = 'var(--danger-color)';
    }
}

function getScoreClass(score) {
    if (score >= 80) return 'good';
    if (score >= 50) return 'medium';
    return 'bad';
}

// ISO 27001
async function loadIso27001Controls() {
    try {
        const category = document.getElementById('category-filter')?.value || '';
        const url = category ? `${API_BASE}/iso27001/controls?category=${category}` : `${API_BASE}/iso27001/controls`;
        const response = await fetch(url);
        iso27001Controls = await response.json();
        
        // Charger aussi les vérifications automatiques pour ce groupe
        if (!realComplianceResults) {
            realComplianceResults = await fetch(`${API_BASE}/checks`).then(r => r.json()).catch(() => null);
        }
        
        renderIso27001Controls();
    } catch (error) {
        console.error('Erreur chargement contrôles ISO 27001:', error);
    }
}

function renderIso27001Controls() {
    const container = document.getElementById('iso27001-controls');
    
    // Grouper par catégorie
    const grouped = {};
    iso27001Controls.forEach(control => {
        if (!grouped[control.category]) {
            grouped[control.category] = [];
        }
        grouped[control.category].push(control);
    });
    
    const categoryNames = {
        'A.5': 'A.5 - Contrôles organisationnels',
        'A.6': 'A.6 - Contrôles du personnel',
        'A.7': 'A.7 - Contrôles physiques',
        'A.8': 'A.8 - Contrôles technologiques'
    };
    
    // Créer une map des vérifications automatiques
    const autoChecks = {};
    if (realComplianceResults && realComplianceResults.checkResults) {
        realComplianceResults.checkResults.forEach(c => {
            autoChecks[c.controlId] = c;
        });
    }
    
    let html = '';
    for (const [category, controls] of Object.entries(grouped)) {
        const implemented = controls.filter(c => c.status === 3 || c.status === 4).length;
        const percentage = Math.round((implemented / controls.length) * 100);
        
        html += `
            <div class="category-header" onclick="toggleCategory('${category}')">
                <span>${categoryNames[category] || category} (${controls.length} contrôles)</span>
                <span>${percentage}% ?</span>
            </div>
            <div class="category-content" id="cat-${category.replace('.', '-')}" style="display:none;">
                <table class="controls-table">
                    <thead>
                        <tr>
                            <th>ID</th>
                            <th>Titre</th>
                            <th>Vérification Auto</th>
                            <th>Statut Manuel</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${controls.map(control => {
                            const autoCheck = autoChecks[control.id];
                            return `
                            <tr>
                                <td><strong>${control.id}</strong></td>
                                <td title="${control.description}">${control.title}</td>
                                <td>
                                    ${autoCheck ? `
                                        <span class="auto-check-badge ${autoCheck.status.toLowerCase()}" 
                                              onclick="showCheckDetails('${control.id}')" 
                                              title="${autoCheck.message}">
                                            ${getStatusEmoji(autoCheck.status)} ${getStatusLabel(autoCheck.status)}
                                        </span>
                                    ` : '<span class="auto-check-badge not-verifiable">Manuel requis</span>'}
                                </td>
                                <td>${getStatusBadge(control.status)}</td>
                                <td>
                                    <select onchange="updateControlStatus('${control.id}', this.value)" style="padding:4px;">
                                        <option value="0" ${control.status === 0 ? 'selected' : ''}>N/A</option>
                                        <option value="1" ${control.status === 1 ? 'selected' : ''}>Non implémenté</option>
                                        <option value="2" ${control.status === 2 ? 'selected' : ''}>Partiel</option>
                                        <option value="3" ${control.status === 3 ? 'selected' : ''}>Implémenté</option>
                                        <option value="4" ${control.status === 4 ? 'selected' : ''}>Efficace</option>
                                    </select>
                                </td>
                            </tr>
                        `}).join('')}
                    </tbody>
                </table>
            </div>
        `;
    }
    
    container.innerHTML = html;
}

function toggleCategory(category) {
    const content = document.getElementById(`cat-${category.replace('.', '-')}`);
    content.style.display = content.style.display === 'none' ? 'block' : 'none';
}

async function updateControlStatus(controlId, status) {
    try {
        await fetch(`${API_BASE}/iso27001/controls/${controlId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: parseInt(status) })
        });
        showNotification('Contrôle mis à jour', 'success');
    } catch (error) {
        showNotification('Erreur mise à jour', 'error');
    }
}

function filterControls() {
    loadIso27001Controls();
}

function getStatusBadge(status) {
    const badges = {
        0: '<span class="status-badge na">N/A</span>',
        1: '<span class="status-badge not-implemented">Non implémenté</span>',
        2: '<span class="status-badge partial">Partiel</span>',
        3: '<span class="status-badge implemented">Implémenté</span>',
        4: '<span class="status-badge implemented">Efficace</span>'
    };
    return badges[status] || badges[1];
}

// ISO 15408
async function loadIso15408Data() {
    try {
        // Charger la cible de sécurité
        const stResponse = await fetch(`${API_BASE}/iso15408/security-target`);
        const securityTarget = await stResponse.json();
        
        // Charger l'évaluation
        const evalResponse = await fetch(`${API_BASE}/iso15408/evaluate`);
        const evaluation = await evalResponse.json();
        
        // Afficher la cible de sécurité
        document.getElementById('security-target').innerHTML = `
            <p><strong>Produit:</strong> ${securityTarget.productName} v${securityTarget.productVersion}</p>
            <p><strong>Description:</strong> ${securityTarget.description}</p>
            <p><strong>Niveau EAL cible:</strong> <span class="eal-badge">EAL${securityTarget.claimedAssuranceLevel}</span></p>
            <p><strong>Statut:</strong> ${getEvaluationStatus(securityTarget.evaluationStatus)}</p>
            <h4>Interfaces:</h4>
            <ul>${securityTarget.toeDescription.interfaces.map(i => `<li>${i}</li>`).join('')}</ul>
        `;
        
        // Afficher l'évaluation
        document.getElementById('eal-evaluation').innerHTML = `
            <p><strong>Niveau atteint:</strong> <span class="eal-badge">EAL${evaluation.achievedLevel}</span></p>
            <p><strong>Score fonctionnel:</strong> ${evaluation.functionalScore}%</p>
            <p><strong>Score assurance:</strong> ${evaluation.assuranceScore}%</p>
            <p><strong>Conforme au niveau cible:</strong> ${evaluation.meetsTargetLevel ? '? Oui' : '? Non'}</p>
            ${evaluation.gaps.length > 0 ? `
                <h4>Lacunes identifiées:</h4>
                <ul>${evaluation.gaps.slice(0, 5).map(g => `<li>${g}</li>`).join('')}</ul>
            ` : ''}
        `;
        
        // Charger les SFR
        const sfrResponse = await fetch(`${API_BASE}/iso15408/functional-requirements`);
        const sfrs = await sfrResponse.json();
        
        document.getElementById('sfr-table').innerHTML = sfrs.map(sfr => `
            <tr>
                <td><span class="sfr-class">${sfr.id}</span></td>
                <td>${sfr.class}</td>
                <td>${sfr.description}</td>
                <td>${getRequirementStatusBadge(sfr.status)}</td>
            </tr>
        `).join('');
        
        // Charger les SAR
        const sarResponse = await fetch(`${API_BASE}/iso15408/assurance-requirements`);
        const sars = await sarResponse.json();
        
        document.getElementById('sar-table').innerHTML = sars.map(sar => `
            <tr>
                <td><span class="sfr-class">${sar.id}</span></td>
                <td>${sar.class}</td>
                <td>${sar.level}</td>
                <td>${sar.description}</td>
                <td>${getRequirementStatusBadge(sar.status)}</td>
            </tr>
        `).join('');
        
        // Charger les menaces
        const threatsResponse = await fetch(`${API_BASE}/iso15408/threats`);
        const threats = await threatsResponse.json();
        
        document.getElementById('threats-list').innerHTML = threats.map(threat => `
            <div class="finding-card medium">
                <strong>${threat.id}: ${threat.name}</strong>
                <p>${threat.description}</p>
                <small><strong>Agent:</strong> ${threat.threatAgent} | <strong>Méthode:</strong> ${threat.attackMethod}</small>
                <br><small><strong>Contre-mesures:</strong> ${threat.countermeasures.join(', ')}</small>
            </div>
        `).join('');
        
    } catch (error) {
        console.error('Erreur chargement ISO 15408:', error);
    }
}

function getRequirementStatusBadge(status) {
    const badges = {
        0: '<span class="status-badge not-implemented">Non traité</span>',
        1: '<span class="status-badge partial">Partiel</span>',
        2: '<span class="status-badge implemented">Complet</span>',
        3: '<span class="status-badge implemented">Vérifié ?</span>'
    };
    return badges[status] || badges[0];
}

function getEvaluationStatus(status) {
    const statuses = ['Non démarré', 'En cours', 'Évalué', 'Certifié', 'Certification expirée'];
    return statuses[status] || 'Inconnu';
}

// Risques
async function loadRisks() {
    try {
        const response = await fetch(`${API_BASE}/risks`);
        const risks = await response.json();
        
        const tbody = document.getElementById('risks-table');
        if (risks.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8">Aucun risque enregistré</td></tr>';
            return;
        }
        
        tbody.innerHTML = risks.map(risk => `
            <tr>
                <td>${risk.id}</td>
                <td>${risk.assetName}</td>
                <td>${risk.threatDescription.substring(0, 50)}...</td>
                <td><strong>${risk.riskScore}</strong></td>
                <td><span class="status-badge ${getRiskLevelClass(risk.riskLevel)}">${getRiskLevelLabel(risk.riskLevel)}</span></td>
                <td>${getTreatmentLabel(risk.treatment)}</td>
                <td>${getRiskStatusLabel(risk.status)}</td>
                <td>
                    <button class="btn btn-small" onclick="editRisk(${risk.id})">??</button>
                    <button class="btn btn-small btn-danger" onclick="deleteRisk(${risk.id})">???</button>
                </td>
            </tr>
        `).join('');
    } catch (error) {
        console.error('Erreur chargement risques:', error);
    }
}

function getRiskLevelClass(level) {
    const classes = ['', 'low', 'medium', 'high', 'critical'];
    return classes[level] || '';
}

function getRiskLevelLabel(level) {
    const labels = ['', 'Faible', 'Moyen', 'Élevé', 'Critique'];
    return labels[level] || '';
}

function getTreatmentLabel(treatment) {
    const labels = ['Accepter', 'Atténuer', 'Transférer', 'Éviter'];
    return labels[treatment] || '';
}

function getRiskStatusLabel(status) {
    const labels = ['Identifié', 'En évaluation', 'Traitement planifié', 'Traitement en cours', 'Traité', 'Accepté', 'Clôturé'];
    return labels[status] || '';
}

function showAddRiskModal() {
    resetRiskForm();
    document.getElementById('risk-modal').style.display = 'block';
}

async function saveRisk(event) {
    event.preventDefault();
    
    const risk = {
        assetName: document.getElementById('risk-asset').value,
        assetType: parseInt(document.getElementById('risk-asset-type').value),
        threatDescription: document.getElementById('risk-threat').value,
        vulnerabilityDescription: document.getElementById('risk-vulnerability').value,
        likelihood: parseInt(document.getElementById('risk-likelihood').value),
        impact: parseInt(document.getElementById('risk-impact').value),
        treatment: parseInt(document.getElementById('risk-treatment').value)
    };
    
    try {
        if (editingRiskId) {
            await fetch(`${API_BASE}/risks/${editingRiskId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(risk)
            });
            showNotification('Risque mis à jour', 'success');
        } else {
            await fetch(`${API_BASE}/risks`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(risk)
            });
            showNotification('Risque ajouté avec succès', 'success');
        }
        
        closeModal('risk-modal');
        resetRiskForm();
        loadRisks();
    } catch (error) {
        showNotification('Erreur lors de l\'enregistrement', 'error');
    }
}

function resetRiskForm() {
    editingRiskId = null;
    document.getElementById('risk-form').reset();
    const modalTitle = document.querySelector('#risk-modal h2');
    if (modalTitle) modalTitle.textContent = 'Ajouter un risque';
}

async function deleteRisk(id) {
    if (!confirm('Supprimer ce risque ?')) return;
    
    try {
        await fetch(`${API_BASE}/risks/${id}`, { method: 'DELETE' });
        loadRisks();
        showNotification('Risque supprimé', 'success');
    } catch (error) {
        showNotification('Erreur suppression', 'error');
    }
}

async function editRisk(id) {
    try {
        const response = await fetch(`${API_BASE}/risks/${id}`);
        if (!response.ok) throw new Error('Risque non trouvé');
        
        const risk = await response.json();
        
        document.getElementById('risk-asset').value = risk.assetName || '';
        document.getElementById('risk-asset-type').value = risk.assetType || 0;
        document.getElementById('risk-threat').value = risk.threatDescription || '';
        document.getElementById('risk-vulnerability').value = risk.vulnerabilityDescription || '';
        document.getElementById('risk-likelihood').value = risk.likelihood || 1;
        document.getElementById('risk-impact').value = risk.impact || 1;
        document.getElementById('risk-treatment').value = risk.treatment || 0;
        
        editingRiskId = id;
        
        const modalTitle = document.querySelector('#risk-modal h2');
        if (modalTitle) modalTitle.textContent = 'Modifier le risque';
        
        document.getElementById('risk-modal').style.display = 'block';
    } catch (error) {
        console.error('Erreur chargement risque:', error);
        showNotification('Erreur lors du chargement du risque', 'error');
    }
}

// Incidents
async function loadIncidents() {
    try {
        const response = await fetch(`${API_BASE}/incidents`);
        const incidents = await response.json();
        
        const tbody = document.getElementById('incidents-table');
        if (incidents.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7">Aucun incident enregistré</td></tr>';
            return;
        }
        
        tbody.innerHTML = incidents.map(incident => `
            <tr>
                <td>${incident.id}</td>
                <td>${incident.title}</td>
                <td>${getIncidentCategoryLabel(incident.category)}</td>
                <td><span class="status-badge ${getSeverityClass(incident.severity)}">${getSeverityLabel(incident.severity)}</span></td>
                <td>${getIncidentStatusLabel(incident.status)}</td>
                <td>${formatDate(incident.detectedAt)}</td>
                <td>
                    <button class="btn btn-small" onclick="viewIncident(${incident.id})">???</button>
                </td>
            </tr>
        `).join('');
    } catch (error) {
        console.error('Erreur chargement incidents:', error);
    }
}

function getIncidentCategoryLabel(category) {
    const labels = ['Malware', 'Accès non autorisé', 'Fuite de données', 'DoS', 'Ingénierie sociale', 'Sécurité physique', 'Violation politique', 'Autre'];
    return labels[category] || 'Autre';
}

function getSeverityLabel(severity) {
    const labels = ['Faible', 'Moyenne', 'Élevée', 'Critique'];
    return labels[severity] || '';
}

function getSeverityClass(severity) {
    const classes = ['low', 'medium', 'high', 'critical'];
    return classes[severity] || '';
}

function getIncidentStatusLabel(status) {
    const labels = ['Nouveau', 'Investigation', 'Confinement', 'Éradication', 'Récupération', 'Leçons apprises', 'Clôturé'];
    return labels[status] || '';
}

function showAddIncidentModal() {
    document.getElementById('incident-modal').style.display = 'block';
}

async function saveIncident(event) {
    event.preventDefault();
    
    const incident = {
        title: document.getElementById('incident-title').value,
        description: document.getElementById('incident-description').value,
        category: parseInt(document.getElementById('incident-category').value),
        severity: parseInt(document.getElementById('incident-severity').value),
        affectedAssets: document.getElementById('incident-assets').value
    };
    
    try {
        await fetch(`${API_BASE}/incidents`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(incident)
        });
        
        closeModal('incident-modal');
        document.getElementById('incident-form').reset();
        loadIncidents();
        showNotification('Incident déclaré', 'success');
    } catch (error) {
        showNotification('Erreur lors de la déclaration', 'error');
    }
}

// Voir les détails d'un incident
async function viewIncident(id) {
    try {
        const response = await fetch(`${API_BASE}/incidents/${id}`);
        if (!response.ok) throw new Error('Incident non trouvé');
        
        const incident = await response.json();
        
        // Créer un modal de visualisation
        const existingModal = document.getElementById('view-incident-modal');
        if (existingModal) existingModal.remove();
        
        const modal = document.createElement('div');
        modal.className = 'modal';
        modal.id = 'view-incident-modal';
        modal.style.display = 'block';
        modal.innerHTML = `
            <div class="modal-content">
                <span class="close" onclick="document.getElementById('view-incident-modal').remove();">&times;</span>
                <h2>?? Incident #${incident.id}</h2>
                <div style="margin-top: 20px;">
                    <p><strong>Titre:</strong> ${incident.title}</p>
                    <p><strong>Description:</strong> ${incident.description || 'Non spécifiée'}</p>
                    <p><strong>Catégorie:</strong> ${getIncidentCategoryLabel(incident.category)}</p>
                    <p><strong>Sévérité:</strong> <span class="status-badge ${getSeverityClass(incident.severity)}">${getSeverityLabel(incident.severity)}</span></p>
                    <p><strong>Statut:</strong> ${getIncidentStatusLabel(incident.status)}</p>
                    <p><strong>Détecté le:</strong> ${formatDate(incident.detectedAt)}</p>
                    ${incident.resolvedAt ? `<p><strong>Résolu le:</strong> ${formatDate(incident.resolvedAt)}</p>` : ''}
                    ${incident.affectedAssets ? `<p><strong>Actifs affectés:</strong> ${incident.affectedAssets}</p>` : ''}
                    ${incident.rootCause ? `<p><strong>Cause racine:</strong> ${incident.rootCause}</p>` : ''}
                    ${incident.lessonsLearned ? `<p><strong>Leçons apprises:</strong> ${incident.lessonsLearned}</p>` : ''}
                </div>
                <div style="margin-top: 20px; display: flex; gap: 10px;">
                    ${incident.status < 6 ? `
                        <button class="btn btn-primary" onclick="updateIncidentStatus(${incident.id}, ${incident.status + 1})">
                            Passer à l'étape suivante
                        </button>
                    ` : ''}
                    <button class="btn btn-secondary" onclick="document.getElementById('view-incident-modal').remove();">
                        Fermer
                    </button>
                </div>
            </div>
        `;
        
        document.body.appendChild(modal);
        
        // Fermer en cliquant dehors
        modal.onclick = function(e) {
            if (e.target === modal) {
                modal.remove();
            }
        };
    } catch (error) {
        console.error('Erreur chargement incident:', error);
        showNotification('Erreur lors du chargement de l\'incident', 'error');
    }
}

// Mettre à jour le statut d'un incident
async function updateIncidentStatus(id, newStatus) {
    try {
        await fetch(`${API_BASE}/incidents/${id}/status`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: newStatus })
        });
        
        showNotification('Statut mis à jour', 'success');
        
        // Fermer le modal et recharger
        const modal = document.getElementById('view-incident-modal');
        if (modal) modal.remove();
        
        loadIncidents();
    } catch (error) {
        showNotification('Erreur mise à jour statut', 'error');
    }
}

// Audits
async function loadAudits() {
    try {
        const response = await fetch(`${API_BASE}/audits`);
        const audits = await response.json();
        
        const timeline = document.getElementById('audit-timeline');
        if (audits.length === 0) {
            timeline.innerHTML = '<p>Aucun audit réalisé</p>';
            return;
        }
        
        timeline.innerHTML = audits.map(audit => `
            <div class="audit-item">
                <strong>${audit.auditId}</strong> - ${audit.standard}
                <br><small>${formatDate(audit.auditDate)} | Type: ${getAuditTypeLabel(audit.auditType)}</small>
                <br><strong>Score: ${audit.overallComplianceScore}%</strong>
                <br><small>${audit.summary || ''}</small>
                <br>
                <small>
                    Constatations: ${audit.findings?.length || 0} | 
                    Contrôles évalués: ${audit.controlAssessments?.length || 0}
                </small>
            </div>
        `).join('');
    } catch (error) {
        console.error('Erreur chargement audits:', error);
    }
}

function getAuditTypeLabel(type) {
    const labels = ['Interne', 'Externe', 'Certification', 'Surveillance', 'Auto-évaluation'];
    return labels[type] || '';
}

async function runAudit(standard) {
    showNotification(`Lancement de l'audit ${standard}...`, 'info');
    
    try {
        const response = await fetch(`${API_BASE}/audits/run?standard=${standard}`, { method: 'POST' });
        const audit = await response.json();
        
        showNotification(`Audit terminé: ${audit.overallComplianceScore}%`, 'success');
        
        // Rafraîchir selon l'onglet actif
        if (currentTab === 'dashboard') loadDashboard();
        if (currentTab === 'audits') loadAudits();
    } catch (error) {
        showNotification('Erreur lors de l\'audit', 'error');
    }
}

async function generateReport(standard) {
    try {
        const response = await fetch(`${API_BASE}/reports/${standard}`);
        const data = await response.json();
        
        // Créer et télécharger le rapport
        const blob = new Blob([data.report], { type: 'text/markdown' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `rapport_${standard}_${new Date().toISOString().split('T')[0]}.md`;
        a.click();
        URL.revokeObjectURL(url);
        
        showNotification('Rapport généré', 'success');
    } catch (error) {
        showNotification('Erreur génération rapport', 'error');
    }
}

// Utilitaires
function closeModal(modalId) {
    document.getElementById(modalId).style.display = 'none';
}

function formatDate(dateString) {
    if (!dateString) return '-';
    return new Date(dateString).toLocaleDateString('fr-FR', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function showNotification(message, type = 'info') {
    const notification = document.createElement('div');
    notification.className = `notification ${type}`;
    notification.innerHTML = `
        <span>${message}</span>
        <button onclick="this.parentElement.remove()">×</button>
    `;
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        padding: 15px 20px;
        border-radius: 8px;
        background: ${type === 'success' ? 'var(--success-color)' : type === 'error' ? 'var(--danger-color)' : 'var(--info-color)'};
        color: white;
        z-index: 10000;
        display: flex;
        align-items: center;
        gap: 10px;
        animation: slideIn 0.3s ease;
    `;
    
    document.body.appendChild(notification);
    
    setTimeout(() => notification.remove(), 5000);
}

window.onclick = function(event) {
    if (event.target.classList.contains('modal')) {
        event.target.style.display = 'none';
    }
};
