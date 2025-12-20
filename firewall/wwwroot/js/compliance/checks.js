// Real Compliance Checks Module
import { API_BASE, formatDate, getStatusEmoji, getStatusLabel, formatDetailKey, formatDetailValue, showNotification } from './utils.js';

let realComplianceResults = null;

export function getRealComplianceResults() {
    return realComplianceResults;
}

export function setRealComplianceResults(results) {
    realComplianceResults = results;
}

export function renderRealComplianceChecks(data) {
    const container = document.getElementById('real-compliance-checks');
    if (!container) return;
    
    const statusIcons = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    
    const byStatus = {
        'NonCompliant': [],
        'PartiallyCompliant': [],
        'Compliant': [],
        'NotVerifiable': [],
        'Error': []
    };
    
    data.checkResults.forEach(r => {
        if (byStatus[r.status]) byStatus[r.status].push(r);
    });
    
    let html = `
        <div class="compliance-summary-bar">
            <div class="summary-stat compliant"><span class="stat-number">${data.compliant}</span><span class="stat-label">Conforme</span></div>
            <div class="summary-stat partial"><span class="stat-number">${data.partiallyCompliant}</span><span class="stat-label">Partiel</span></div>
            <div class="summary-stat non-compliant"><span class="stat-number">${data.nonCompliant}</span><span class="stat-label">Non conforme</span></div>
            <div class="summary-stat not-verifiable"><span class="stat-number">${data.notVerifiable}</span><span class="stat-label">N/A</span></div>
        </div>
        <div class="compliance-checks-grid">
    `;
    
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
    
    html += `</div><div class="check-timestamp">Dernière vérification: ${formatDate(data.checkedAt)}</div>`;
    container.innerHTML = html;
}

export async function showCheckDetails(controlId) {
    try {
        const result = await fetch(`${API_BASE}/checks/${controlId}`).then(r => r.json());
        
        let detailsHtml = '<div class="check-details-grid">';
        for (const [key, value] of Object.entries(result.details || {})) {
            detailsHtml += `<div class="detail-item"><span class="detail-key">${formatDetailKey(key)}</span><span class="detail-value">${formatDetailValue(value)}</span></div>`;
        }
        detailsHtml += '</div>';
        
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
        modal.onclick = function(e) { if (e.target === modal) modal.remove(); };
    } catch (error) {
        console.error('Erreur chargement détails:', error);
    }
}

export async function runComplianceChecks() {
    const container = document.getElementById('real-compliance-checks');
    if (container) {
        container.innerHTML = '<div class="loading"><i class="fas fa-spinner fa-spin"></i> Vérification en cours...</div>';
    }
    
    try {
        const results = await fetch(`${API_BASE}/checks/run`, { method: 'POST' }).then(r => r.json());
        realComplianceResults = { checkResults: results };
        
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
        if (container) container.innerHTML = '<div class="error">Erreur lors de la vérification</div>';
        showNotification('Erreur lors de la vérification', 'error');
    }
}
