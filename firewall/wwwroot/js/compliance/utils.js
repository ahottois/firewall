// Compliance Utils - Fonctions utilitaires
export const API_BASE = '/api/compliance';

export function formatDate(dateString) {
    if (!dateString) return '-';
    return new Date(dateString).toLocaleDateString('fr-FR', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

export function getScoreClass(score) {
    if (score >= 80) return 'good';
    if (score >= 50) return 'medium';
    return 'bad';
}

export function updateProgressRing(id, percentage) {
    const circle = document.getElementById(id);
    if (!circle) return;
    
    const circumference = 2 * Math.PI * 65;
    circle.style.strokeDasharray = circumference;
    circle.style.strokeDashoffset = circumference - (percentage / 100) * circumference;
    
    if (percentage >= 80) {
        circle.style.stroke = 'var(--success-color)';
    } else if (percentage >= 50) {
        circle.style.stroke = 'var(--warning-color)';
    } else {
        circle.style.stroke = 'var(--danger-color)';
    }
}

export function showNotification(message, type = 'info') {
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

export function closeModal(modalId) {
    document.getElementById(modalId).style.display = 'none';
}

export function getStatusBadge(status) {
    const badges = {
        0: '<span class="status-badge na">N/A</span>',
        1: '<span class="status-badge not-implemented">Non implémenté</span>',
        2: '<span class="status-badge partial">Partiel</span>',
        3: '<span class="status-badge implemented">Implémenté</span>',
        4: '<span class="status-badge implemented">Efficace</span>'
    };
    return badges[status] || badges[1];
}

export function getSeverityLabel(severity) {
    const labels = ['Faible', 'Moyenne', 'Élevée', 'Critique'];
    return labels[severity] || '';
}

export function getSeverityClass(severity) {
    const classes = ['low', 'medium', 'high', 'critical'];
    return classes[severity] || '';
}

export function getStatusEmoji(status) {
    const emojis = {
        'Compliant': '?',
        'PartiallyCompliant': '??',
        'NonCompliant': '?',
        'NotVerifiable': '?',
        'Error': '??'
    };
    return emojis[status] || '?';
}

export function getStatusLabel(status) {
    const labels = {
        'Compliant': 'Conforme',
        'PartiallyCompliant': 'Partiellement conforme',
        'NonCompliant': 'Non conforme',
        'NotVerifiable': 'Non vérifiable',
        'Error': 'Erreur'
    };
    return labels[status] || status;
}

export function formatDetailKey(key) {
    return key
        .replace(/([A-Z])/g, ' $1')
        .replace(/^./, str => str.toUpperCase())
        .trim();
}

export function formatDetailValue(value) {
    if (typeof value === 'boolean') return value ? '? Oui' : '? Non';
    if (Array.isArray(value)) return value.join(', ') || '-';
    if (typeof value === 'object') return JSON.stringify(value, null, 2);
    return value ?? '-';
}
