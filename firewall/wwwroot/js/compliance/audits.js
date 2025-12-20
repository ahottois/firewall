// Audits Module
import { API_BASE, formatDate, showNotification } from './utils.js';

export async function loadAudits() {
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
                <br><small>Constatations: ${audit.findings?.length || 0} | Contrôles évalués: ${audit.controlAssessments?.length || 0}</small>
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

export async function runAudit(standard, currentTab, loadDashboard) {
    showNotification(`Lancement de l'audit ${standard}...`, 'info');
    
    try {
        const response = await fetch(`${API_BASE}/audits/run?standard=${standard}`, { method: 'POST' });
        const audit = await response.json();
        
        showNotification(`Audit terminé: ${audit.overallComplianceScore}%`, 'success');
        
        if (currentTab === 'dashboard') loadDashboard();
        if (currentTab === 'audits') loadAudits();
    } catch (error) {
        showNotification('Erreur lors de l\'audit', 'error');
    }
}

export async function generateReport(standard) {
    try {
        const response = await fetch(`${API_BASE}/reports/${standard}`);
        const data = await response.json();
        
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
