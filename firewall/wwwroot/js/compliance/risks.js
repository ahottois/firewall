// Risks Module
import { API_BASE, formatDate, showNotification, closeModal } from './utils.js';

let editingRiskId = null;

export async function loadRisks() {
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

export function showAddRiskModal() {
    resetRiskForm();
    document.getElementById('risk-modal').style.display = 'block';
}

export async function saveRisk(event) {
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

export function resetRiskForm() {
    editingRiskId = null;
    document.getElementById('risk-form').reset();
    const modalTitle = document.querySelector('#risk-modal h2');
    if (modalTitle) modalTitle.textContent = 'Ajouter un risque';
}

export async function deleteRisk(id) {
    if (!confirm('Supprimer ce risque ?')) return;
    
    try {
        await fetch(`${API_BASE}/risks/${id}`, { method: 'DELETE' });
        loadRisks();
        showNotification('Risque supprimé', 'success');
    } catch (error) {
        showNotification('Erreur suppression', 'error');
    }
}

export async function editRisk(id) {
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
