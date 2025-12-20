// Incidents Module
import { API_BASE, formatDate, showNotification, closeModal, getSeverityLabel, getSeverityClass } from './utils.js';

export async function loadIncidents() {
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
                <td><button class="btn btn-small" onclick="viewIncident(${incident.id})">???</button></td>
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

function getIncidentStatusLabel(status) {
    const labels = ['Nouveau', 'Investigation', 'Confinement', 'Éradication', 'Récupération', 'Leçons apprises', 'Clôturé'];
    return labels[status] || '';
}

export function showAddIncidentModal() {
    document.getElementById('incident-modal').style.display = 'block';
}

export async function saveIncident(event) {
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

export async function viewIncident(id) {
    try {
        const response = await fetch(`${API_BASE}/incidents/${id}`);
        if (!response.ok) throw new Error('Incident non trouvé');
        
        const incident = await response.json();
        
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
                </div>
                <div style="margin-top: 20px; display: flex; gap: 10px;">
                    ${incident.status < 6 ? `<button class="btn btn-primary" onclick="updateIncidentStatus(${incident.id}, ${incident.status + 1})">Passer à l'étape suivante</button>` : ''}
                    <button class="btn btn-secondary" onclick="document.getElementById('view-incident-modal').remove();">Fermer</button>
                </div>
            </div>
        `;
        
        document.body.appendChild(modal);
        modal.onclick = function(e) { if (e.target === modal) modal.remove(); };
    } catch (error) {
        console.error('Erreur chargement incident:', error);
        showNotification('Erreur lors du chargement de l\'incident', 'error');
    }
}

export async function updateIncidentStatus(id, newStatus) {
    try {
        await fetch(`${API_BASE}/incidents/${id}/status`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: newStatus })
        });
        
        showNotification('Statut mis à jour', 'success');
        
        const modal = document.getElementById('view-incident-modal');
        if (modal) modal.remove();
        
        loadIncidents();
    } catch (error) {
        showNotification('Erreur mise à jour statut', 'error');
    }
}
