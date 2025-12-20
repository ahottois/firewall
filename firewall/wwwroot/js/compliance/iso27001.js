// ISO 27001 Module
import { API_BASE, formatDate, getStatusBadge, getStatusEmoji, getStatusLabel, showNotification } from './utils.js';

let iso27001Controls = [];
let realComplianceResults = null;

export function setRealComplianceResults(results) {
    realComplianceResults = results;
}

export async function loadIso27001Controls() {
    try {
        const category = document.getElementById('category-filter')?.value || '';
        const url = category ? `${API_BASE}/iso27001/controls?category=${category}` : `${API_BASE}/iso27001/controls`;
        const response = await fetch(url);
        iso27001Controls = await response.json();
        
        if (!realComplianceResults) {
            realComplianceResults = await fetch(`${API_BASE}/checks`).then(r => r.json()).catch(() => null);
        }
        
        renderIso27001Controls();
    } catch (error) {
        console.error('Erreur chargement contrôles ISO 27001:', error);
    }
}

export function renderIso27001Controls() {
    const container = document.getElementById('iso27001-controls');
    
    const grouped = {};
    iso27001Controls.forEach(control => {
        if (!grouped[control.category]) grouped[control.category] = [];
        grouped[control.category].push(control);
    });
    
    const categoryNames = {
        'A.5': 'A.5 - Contrôles organisationnels',
        'A.6': 'A.6 - Contrôles du personnel',
        'A.7': 'A.7 - Contrôles physiques',
        'A.8': 'A.8 - Contrôles technologiques'
    };
    
    const autoChecks = {};
    if (realComplianceResults?.checkResults) {
        realComplianceResults.checkResults.forEach(c => autoChecks[c.controlId] = c);
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
                        <tr><th>ID</th><th>Titre</th><th>Vérification Auto</th><th>Statut Manuel</th><th>Actions</th></tr>
                    </thead>
                    <tbody>
                        ${controls.map(control => {
                            const autoCheck = autoChecks[control.id];
                            return `
                            <tr>
                                <td><strong>${control.id}</strong></td>
                                <td title="${control.description}">${control.title}</td>
                                <td>${autoCheck ? `
                                    <span class="auto-check-badge ${autoCheck.status.toLowerCase()}" 
                                          onclick="showCheckDetails('${control.id}')" 
                                          title="${autoCheck.message}">
                                        ${getStatusEmoji(autoCheck.status)} ${getStatusLabel(autoCheck.status)}
                                    </span>` : '<span class="auto-check-badge not-verifiable">Manuel requis</span>'}
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
                            </tr>`;
                        }).join('')}
                    </tbody>
                </table>
            </div>
        `;
    }
    
    container.innerHTML = html;
}

export function toggleCategory(category) {
    const content = document.getElementById(`cat-${category.replace('.', '-')}`);
    content.style.display = content.style.display === 'none' ? 'block' : 'none';
}

export async function updateControlStatus(controlId, status) {
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

export function filterControls() {
    loadIso27001Controls();
}
