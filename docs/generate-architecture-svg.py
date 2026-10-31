import re, glob, os
from xml.sax.saxutils import escape

# Usage: python generate-architecture-svg.py <path to extracted Azure_Public_Service_Icons\Icons>
import sys
ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get('TEMP', '.'), 'azicons', 'x', 'Azure_Public_Service_Icons', 'Icons')
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(OUT_DIR, 'solution-architecture.svg')
OUT2 = os.path.join(OUT_DIR, 'agentic-ai.svg')

ICON_FILES = {
    'swa': 'web/01007-icon-service-Static-Apps.svg',
    'entra': 'identity/10232-icon-service-App-Registrations.svg',
    'aca': 'other/02989-icon-service-Container-Apps-Environments.svg',
    'acr': 'containers/10105-icon-service-Container-Registries.svg',
    'pg': 'databases/10131-icon-service-Azure-Database-PostgreSQL-Server.svg',
    'blob': 'storage/10086-icon-service-Storage-Accounts.svg',
    'mi': 'identity/10227-icon-service-Managed-Identities.svg',
    'di': 'ai + machine learning/02749-icon-service-Azure-Applied-AI-Services.svg',
    'aoai': 'ai + machine learning/03438-icon-service-Azure-OpenAI.svg',
    'agent': 'ai + machine learning/10165-icon-service-Bot-Services.svg',
    'law': 'analytics/00009-icon-service-Log-Analytics-Workspaces.svg',
    'dns': 'networking/10064-icon-service-DNS-Zones.svg',
}

defs = []
for key, rel in ICON_FILES.items():
    svg = open(os.path.join(ROOT, rel), encoding='utf-8').read()
    svg = re.sub(r'<\?xml.*?\?>', '', svg).strip()
    # prefix ids so gradients from different icons do not collide
    svg = re.sub(r'id="([^"]+)"', lambda m: f'id="{key}_{m.group(1)}"', svg)
    svg = re.sub(r'url\(#([^)]+)\)', lambda m: f'url(#{key}_{m.group(1)})', svg)
    svg = re.sub(r'href="#([^"]+)"', lambda m: f'href="#{key}_{m.group(1)}"', svg)
    inner = re.sub(r'^<svg[^>]*>|</svg>$', '', svg).strip()
    defs.append(f'<symbol id="ic-{key}" viewBox="0 0 18 18">{inner}</symbol>')

W, H = 1720, 1010
o = []
a = o.append

COL = {'azure': '#0078d4', 'svc': '#0f766e', 'agent': '#7c3aed', 'ext': '#6b7280'}
FILL = {'azure': '#eff6fc', 'svc': '#ecfdf5', 'agent': '#f5f0ff', 'ext': '#f3f4f6'}


TAG_TOP = False


def icon(key, x, y, s=36):
    a(f'<use href="#ic-{key}" x="{x}" y="{y}" width="{s}" height="{s}"/>')


def card(x, y, w, h, title, sub, kind, ic=None, dashed=False, tag=None):
    dash = ' stroke-dasharray="6 4"' if dashed else ''
    a(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{FILL[kind]}" stroke="{COL[kind]}" stroke-width="2"{dash}/>')
    tx = x + 14
    if ic:
        icon(ic, x + 12, y + 12, 36)
        tx = x + 58
    a(f'<text x="{tx}" y="{y + 30}" class="t">{escape(title)}</text>')
    for i, line in enumerate(sub.split('\n')):
        a(f'<text x="{tx}" y="{y + 50 + i * 16}" class="s">{escape(line)}</text>')
    if tag:
        a(f'<text x="{x + w - 10}" y="{y + (16 if TAG_TOP else h - 8)}" class="tag" text-anchor="end" fill="{COL[kind]}">{escape(tag)}</text>')


def arrow(pts, label=None, lx=None, ly=None, dashed=False, color='#374151', anchor='middle'):
    d = 'M' + ' L'.join(f'{px},{py}' for px, py in pts)
    dash = ' stroke-dasharray="5 4"' if dashed else ''
    a(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="1.8"{dash} marker-end="url(#ah)"/>')
    if label:
        a(f'<text x="{lx}" y="{ly}" class="l" text-anchor="{anchor}">{escape(label)}</text>')


a(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" font-family="Segoe UI, Arial, sans-serif">')
a('<title>Dokumenta DMS Cloud - Solution Architecture</title>')
a('<defs>' + ''.join(defs))
a('<marker id="ah" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#374151"/></marker>')
a('<style>.t{font-size:15px;font-weight:600;fill:#111827}.s{font-size:12px;fill:#4b5563}.l{font-size:12px;fill:#374151;paint-order:stroke;stroke:#fff;stroke-width:4px}.tag{font-size:10px;font-weight:600;text-transform:uppercase}.h{font-size:13px;font-weight:600;fill:#0078d4}.title{font-size:24px;font-weight:700;fill:#111827}</style>')
a('</defs>')
a(f'<rect width="{W}" height="{H}" fill="#ffffff"/>')
a('<text x="40" y="44" class="title">Dokumenta DMS Cloud - Solution Architecture</text>')

# --- edge: user, SWA, Entra ---
a('<circle cx="90" cy="104" r="14" fill="#6b7280"/><path d="M62,152 Q62,126 90,126 Q118,126 118,152 Z" fill="#6b7280"/>')
a('<text x="140" y="118" class="t">User browser</text><text x="140" y="136" class="s">Authenticated tenant user</text>')
card(380, 76, 270, 80, 'Angular SPA', 'Azure Static Web Apps\ndocumenta.giddyitsolutions.com', 'azure', 'swa')
card(730, 76, 270, 80, 'Microsoft Entra ID', 'OIDC sign-in, bearer tokens\napp roles Viewer to Admin', 'azure', 'entra')
arrow([(260, 112), (380, 112)], 'HTTPS', 320, 104)
arrow([(650, 116), (730, 116)], 'OIDC', 690, 108)

# --- Container Apps environment ---
a('<rect x="40" y="200" width="1190" height="600" rx="14" fill="#fafafa" stroke="#0078d4" stroke-width="2" stroke-dasharray="8 5"/>')
icon('aca', 52, 208, 30)
a('<text x="1210" y="229" class="h" text-anchor="end">Azure Container Apps environment (dokumenta-env)  -  Log Analytics for console logs</text>')

card(70, 260, 330, 110, 'DocuManagementApp', 'docmanager - .NET API, external ingress\nupload, revisions, search proxy\nOffice to PDF (LibreOffice), PDF/A (Ghostscript)', 'svc', 'aca', tag='microservice')
card(70, 410, 330, 90, 'veraPDF REST', 'verapdf - internal\nindependent PDF/A-2b validation', 'svc', 'aca', tag='container')
card(480, 260, 250, 110, 'RabbitMQ', 'rabbitmq - internal TCP 5672\nevent queues between services', 'svc', 'aca', tag='container')
card(480, 410, 250, 100, 'OrchestratorService', 'orchestrator - worker, no ingress\ndeterministic routing\nby document kind', 'svc', 'aca', tag='microservice')

card(840, 240, 360, 290, 'ExtractionService', 'extraction - internal\nlayout, extraction, validation (max 3 attempts)', 'svc', 'aca', tag='microservice')
card(860, 324, 320, 56, 'Classifier agent', 'unknown kind: kind + confidence', 'agent', 'agent', dashed=True, tag='agent')
card(860, 390, 320, 56, 'Extractor agent', 'structured JSON per document kind', 'agent', 'agent', dashed=True, tag='agent')
card(860, 456, 320, 56, 'Corrector agent', 'fixes validator issues', 'agent', 'agent', dashed=True, tag='agent')

card(840, 560, 360, 220, 'SearchService', 'search - internal\nchunks, embeds, deterministic / hybrid /\nagentic search (tenant-scoped)', 'svc', 'aca', tag='microservice')
card(860, 650, 320, 100, 'Search agent', 'DocumentSearchAgent, one read-only tool:\nexecute_hybrid_search -> grounded answer\nwith source citations', 'agent', 'agent', dashed=True, tag='agent')

# --- AI services on the right ---
a('<text x="1270" y="229" class="h">AI services</text>')
card(1270, 250, 420, 80, 'Azure Document Intelligence', 'layout analysis', 'azure', 'di')
card(1270, 350, 420, 80, 'Gemini API', 'classification, extraction, correction', 'ext', tag='external')
card(1270, 450, 420, 80, 'OpenAI embeddings', 'text-embedding-3-small', 'ext', tag='external')
card(1270, 550, 420, 80, 'Azure OpenAI', 'gpt-5.4-mini - agentic search chat', 'azure', 'aoai')

# --- data & platform row ---
a('<text x="40" y="826" class="h">Data and platform</text>')
card(40, 840, 250, 90, 'PostgreSQL Flexible Server', '4 databases, pgvector + pg_trgm', 'azure', 'pg')
card(320, 840, 250, 90, 'Blob Storage', 'private "documents" container', 'azure', 'blob')
card(600, 840, 250, 90, 'Managed identity', 'ACR pull, Blob Data Contributor', 'azure', 'mi')
card(880, 840, 250, 90, 'Container Registry', 'service images (az acr build)', 'azure', 'acr')
card(1160, 840, 250, 90, 'Log Analytics', 'Container Apps logs', 'azure', 'law')
card(1440, 840, 250, 90, 'Azure DNS zone', 'public zone; CNAME to the SPA', 'azure', 'dns')

# --- flows ---
arrow([(230, 156), (230, 260)], None)
arrow([(235, 370), (235, 410)], 'PDF/A check', 300, 395)
arrow([(400, 315), (480, 315)], 'outbox', 440, 307)
arrow([(605, 370), (605, 410)], 'ingested', 640, 395)
arrow([(730, 300), (840, 300)], 'extract request', 785, 292)
arrow([(730, 350), (810, 350), (810, 640), (840, 640)], 'index request', 804, 600, anchor='end')
arrow([(70, 345), (52, 345), (52, 720), (840, 720)], 'internal search proxy + API key', 330, 712, anchor='middle')
arrow([(1200, 310), (1270, 290)], None)
arrow([(1200, 400), (1270, 390)], None)
arrow([(1200, 600), (1270, 490)], 'embed', 1236, 545)
arrow([(1200, 700), (1270, 610)], 'chat', 1244, 668)
arrow([(840, 470), (760, 470), (760, 385), (730, 385)], 'artifacts', 766, 440, anchor='start')
arrow([(250, 800), (250, 840)], 'service DBs', 290, 822)
arrow([(430, 800), (430, 840)], 'managed identity', 500, 822)
arrow([(1005, 840), (1005, 800)], 'image pull', 1043, 822, dashed=True)
arrow([(1285, 840), (1285, 800)], 'logs', 1313, 822, dashed=True)


# --- legend ---
a('<text x="40" y="962" class="h">Legend</text>')
lx = 110
for kind, label, dashed in [('azure', 'Azure resource', False), ('svc', 'Microservice / Container App', False),
                            ('agent', 'AI agent', True), ('ext', 'External service', False)]:
    dash = ' stroke-dasharray="6 4"' if dashed else ''
    a(f'<rect x="{lx}" y="948" width="26" height="18" rx="4" fill="{FILL[kind]}" stroke="{COL[kind]}" stroke-width="2"{dash}/>')
    a(f'<text x="{lx + 34}" y="962" class="s">{label}</text>')
    lx += 240
a('<text x="40" y="994" class="s">Azure icons: Microsoft Azure Architecture Icons. Container Apps use the Container Apps Environments icon; agents use the Bot Services icon.</text>')
a('</svg>')

os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, 'w', encoding='utf-8').write('\n'.join(o))
print('written', OUT)

# ===================== Agentic AI diagram =====================
W, H = 1720, 880
TAG_TOP = True
o.clear()
a(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" font-family="Segoe UI, Arial, sans-serif">')
a('<title>Dokumenta DMS Cloud - Agentic AI</title>')
a('<defs>' + ''.join(defs))
a('<marker id="ah" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#374151"/></marker>')
a('<style>.t{font-size:15px;font-weight:600;fill:#111827}.s{font-size:12px;fill:#4b5563}.l{font-size:12px;fill:#374151;paint-order:stroke;stroke:#fff;stroke-width:4px}.tag{font-size:10px;font-weight:600;text-transform:uppercase}.h{font-size:14px;font-weight:600;fill:#0078d4}.title{font-size:24px;font-weight:700;fill:#111827}</style>')
a('</defs>')
a(f'<rect width="{W}" height="{H}" fill="#ffffff"/>')
a('<text x="40" y="44" class="title">Dokumenta DMS Cloud - Agentic AI</text>')

# --- lane 1: extraction ---
a('<text x="40" y="84" class="h">1. Extraction: agents propose, deterministic code validates (ExtractionService)</text>')
card(40, 110, 230, 84, 'Orchestrator', 'document ingested\ndeterministic routing by kind', 'svc', 'aca', tag='microservice')
card(320, 110, 230, 84, 'Extraction job', 'ExtractionService\nvia RabbitMQ', 'svc', 'aca', tag='microservice')
card(600, 110, 230, 84, 'Document Intelligence', 'layout analysis', 'azure', 'di')
card(880, 110, 230, 84, 'Classifier agent', 'unknown kind only:\nkind + confidence (Gemini)', 'agent', 'agent', dashed=True, tag='agent')
card(1160, 110, 230, 84, 'Extractor agent', 'structured JSON for\nthe document kind (Gemini)', 'agent', 'agent', dashed=True, tag='agent')
card(1440, 110, 230, 84, 'Validators', 'check extracted JSON\n(deterministic)', 'svc', 'aca', tag='code')
card(880, 260, 230, 84, 'Manual review', 'low-confidence or\nunsupported kind', 'ext', tag='human')
card(1160, 260, 230, 84, 'Store result', 'status + artifacts, then\npublish index request', 'svc', 'aca', tag='microservice')
card(1440, 260, 230, 84, 'Corrector agent', 'validation issues + previous\ncandidate -> fixed result', 'agent', 'agent', dashed=True, tag='agent')

arrow([(270, 152), (320, 152)], None)
arrow([(550, 152), (600, 152)], None)
arrow([(830, 152), (880, 152)], 'unknown', 855, 142)
arrow([(715, 110), (715, 96), (1275, 96), (1275, 110)], 'known kind: routed deterministically', 995, 90)
arrow([(1110, 152), (1160, 152)], None)
arrow([(1390, 152), (1440, 152)], None)
arrow([(995, 194), (995, 260)], 'low confidence', 1005, 232, anchor='start')
arrow([(1440, 182), (1415, 182), (1415, 302), (1390, 302)], 'valid / 3 attempts used', 1330, 244)
arrow([(1500, 194), (1500, 260)], 'issues', 1510, 232, anchor='start')
arrow([(1610, 260), (1610, 194)], 'retry (max 3)', 1620, 232, anchor='start')

# --- lane 2: agentic search ---
a('<line x1="40" y1="388" x2="1680" y2="388" stroke="#d1d5db" stroke-dasharray="4 4"/>')
a('<text x="40" y="424" class="h">2. Agentic search: one read-only tool, tenant-scoped (SearchService)</text>')
card(40, 450, 230, 84, 'Search page', 'Angular SPA\nnatural-language question', 'azure', 'swa')
card(320, 450, 230, 84, 'DocuManagementApp', 'internal search proxy\ntenant from the token', 'svc', 'aca', tag='microservice')
card(600, 450, 230, 84, 'Search agent', 'DocumentSearchAgent\nno tenant selector', 'agent', 'agent', dashed=True, tag='agent')
card(880, 450, 230, 84, 'execute_hybrid_search', 'semanticQuery, kind, entity,\ndates, limit; max 2 calls', 'svc', 'aca', tag='tool')
card(1160, 450, 230, 84, 'PostgreSQL', 'tenant-scoped hybrid search\npgvector + pg_trgm', 'azure', 'pg')
card(600, 600, 230, 84, 'Azure OpenAI', 'gpt-5.4-mini chat\n(needs agentic API key)', 'azure', 'aoai')
card(880, 600, 230, 84, 'OpenAI embeddings', 'text-embedding-3-small\nquery embedding', 'ext', tag='external')
card(40, 600, 230, 84, 'Results', 'grounded answer with sources;\none row per source document', 'azure', 'swa')

arrow([(270, 492), (320, 492)], None)
arrow([(550, 492), (600, 492)], None)
arrow([(830, 492), (880, 492)], 'tool call', 855, 482)
arrow([(1110, 492), (1160, 492)], 'SQL', 1135, 482)
arrow([(715, 534), (715, 600)], 'chat', 735, 572, anchor='start')
arrow([(995, 534), (995, 600)], 'embed', 1015, 572, anchor='start')
arrow([(155, 534), (155, 600)], 'answer + sources', 170, 572, anchor='start')
arrow([(1275, 344), (1275, 450)], 'index request: chunk, embed, store', 1285, 400, anchor='start')

a('<text x="40" y="730" class="h">Guardrails</text>')
for i, line in enumerate([
    'Agent output is a proposal: extraction results are validated and the correction loop is bounded to three attempts.',
    'The search agent can only call execute_hybrid_search; it cannot issue SQL, select a tenant or call other services.',
    'Answers must be grounded in retrieved chunks and cite sources. The UI shows only the source documents, with links to open or edit them in review.',
    'Agentic search is enabled only when its Azure OpenAI key is configured; deterministic and hybrid search do not depend on it.']):
    a(f'<text x="40" y="{756 + i * 20}" class="s">- {escape(line)}</text>')

a('<text x="40" y="866" class="s">Azure icons: Microsoft Azure Architecture Icons. Dashed purple = AI agent; green = microservice/Container App; blue = Azure resource; grey = external or human step.</text>')
a('</svg>')
open(OUT2, 'w', encoding='utf-8').write('\n'.join(o))
print('written', OUT2)

