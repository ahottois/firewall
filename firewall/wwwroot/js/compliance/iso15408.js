// ISO 15408 Module
import { API_BASE, formatDate, getStatusBadge } from './utils.js';

export async function loadIso15408Data() {
    try {
        const [stResponse, evalResponse, sfrResponse, sarResponse, threatsResponse] = await Promise.all([
            fetch(`${API_BASE}/iso15408/security-target`),
            fetch(`${API_BASE}/iso15408/evaluate`),
            fetch(`${API_BASE}/iso15408/functional-requirements`),
            fetch(`${API_BASE}/iso15408/assurance-requirements`),
            fetch(`${API_BASE}/iso15408/threats`)
        ]);
        
        const [securityTarget, evaluation, sfrs, sars, threats] = await Promise.all([
            stResponse.json(), evalResponse.json(), sfrResponse.json(), sarResponse.json(), threatsResponse.json()
        ]);
        
        renderSecurityTarget(securityTarget);
        renderEvaluation(evaluation);
        renderSfrs(sfrs);
        renderSars(sars);
        renderThreats(threats);
    } catch (error) {
        console.error('Erreur chargement ISO 15408:', error);
    }
}

function renderSecurityTarget(st) {
    document.getElementById('security-target').innerHTML = `
        <p><strong>Produit:</strong> ${st.productName} v${st.productVersion}</p>
        <p><strong>Description:</strong> ${st.description}</p>
        <p><strong>Niveau EAL cible:</strong> <span class="eal-badge">EAL${st.claimedAssuranceLevel}</span></p>
        <p><strong>Statut:</strong> ${getEvaluationStatus(st.evaluationStatus)}</p>
        <h4>Interfaces:</h4>
        <ul>${st.toeDescription.interfaces.map(i => `<li>${i}</li>`).join('')}</ul>
    `;
}

function renderEvaluation(evaluation) {
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
}

function renderSfrs(sfrs) {
    document.getElementById('sfr-table').innerHTML = sfrs.map(sfr => `
        <tr>
            <td><span class="sfr-class">${sfr.id}</span></td>
            <td>${sfr.class}</td>
            <td>${sfr.description}</td>
            <td>${getRequirementStatusBadge(sfr.status)}</td>
        </tr>
    `).join('');
}

function renderSars(sars) {
    document.getElementById('sar-table').innerHTML = sars.map(sar => `
        <tr>
            <td><span class="sfr-class">${sar.id}</span></td>
            <td>${sar.class}</td>
            <td>${sar.level}</td>
            <td>${sar.description}</td>
            <td>${getRequirementStatusBadge(sar.status)}</td>
        </tr>
    `).join('');
}

function renderThreats(threats) {
    document.getElementById('threats-list').innerHTML = threats.map(threat => `
        <div class="finding-card medium">
            <strong>${threat.id}: ${threat.name}</strong>
            <p>${threat.description}</p>
            <small><strong>Agent:</strong> ${threat.threatAgent} | <strong>Méthode:</strong> ${threat.attackMethod}</small>
            <br><small><strong>Contre-mesures:</strong> ${threat.countermeasures.join(', ')}</small>
        </div>
    `).join('');
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
