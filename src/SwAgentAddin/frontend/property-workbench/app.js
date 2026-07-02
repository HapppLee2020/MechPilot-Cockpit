/**
 * MechPilot AICockpit — 主逻辑
 * 功能：Sidebar 导航、多页面路由、AI 侧栏、设计树、选中对象状态、RAG 配置、WebView2 bridge
 */
(function () {
  'use strict';

  // ══════════════════════════════════════════════════════════
  //  页面配置
  // ══════════════════════════════════════════════════════════
  var PAGES = {
    dashboard:  { title: '系统总览',       render: renderDashboard },
    workspace:  { title: '任务编排',   render: renderWorkspace },
    assistant:  { title: 'AI助手',     render: renderAssistant },
    drawing:    { title: '图纸审核',   render: renderDrawing },
    selection:  { title: '快速选型',   render: renderSelection },
    material:   { title: '物料检索',   render: renderMaterial },
    design:     { title: '设计计算',   render: renderDesign },
    agent:      { title: '任务管理',  render: renderAgent },
    settings:   { title: '设置选项',       render: renderSettings }
  };

  var DEFAULT_PAGE = 'workspace';

  // ══════════════════════════════════════════════════════════
  //  SVG 图标
  // ══════════════════════════════════════════════════════════
  var ICONS = {
    chevron: '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 4l4 4-4 4"/></svg>',
    assembly: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="2" width="12" height="12" rx="2" stroke="currentColor" stroke-width="1.4"/><path d="M5 6h6M5 8h4M5 10h5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
    part: '<svg viewBox="0 0 16 16" fill="none"><rect x="3" y="2" width="10" height="12" rx="1.5" stroke="currentColor" stroke-width="1.4"/><path d="M5.5 5h5M5.5 7.5h4" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
    warning: '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M8 2l6 11H2z"/><path d="M8 6v4M8 11.5v.5"/></svg>'
  };

  // ══════════════════════════════════════════════════════════
  //  状态
  // ══════════════════════════════════════════════════════════
  var state = {
    context: null,
    currentPage: DEFAULT_PAGE,
    selectedNode: null,        // 当前高亮节点（右侧摘要展示）
    checkedNodeIds: new Set(), // 勾选节点（批量属性审核等）
    expandedSet: new Set(),    // 设计树展开状态
    aiPanelOpen: false,          // CKP-004-22: AI Panel 默认折叠
    sidebarCollapsed: true,    // CKP-004-19: 左侧导航栏默认折叠
    aiMessages: [],
    hermesOnline: false,      // legacy, keep for compat; prefer hermesStatus
    // CKP-004-10: Hermes status model
    hermesStatus: {
      status: 'unknown',     // unknown | checking | online | auth_required | reachable_wrong_method | offline | error
      message: '',
      base_url: '',
      endpoint: '',
      http_status: null,
      checked_at: null,
      duration_ms: null
    },
    ragOnline: false,          // RAG 服务状态
    tasks: [],
    submittedJobs: [],
    activeJob: null,
    // Context snapshot model (CKP-004-04)
    snapshots: [],
    currentSnapshotId: null,
    taskDrafts: [],
    aiThreads: [],
    currentTaskId: null,       // 当前选中的任务 ID
    taskQueueFilter: 'all',    // CKP-004-13: 'all' | 'draft' | 'queued' | 'running' | 'completed'
    expandedTaskIds: {},       // CKP-004-13: taskId -> true for detail expand
    taskQueueCollapsed: false,   // CKP-004-22: 任务队列折叠
    treeFilters: {                     // 设计树筛选
      lightweight: true,   // 轻化
      hidden: false,       // 隐藏
      suppressed: false,   // 压缩
      envelope: false,     // 封套
      virtualComp: false,  // 虚拟
      readOnly: true       // 只读
    },
    toastMessage: null,        // 页面内 toast 提示
    windowPinned: false,       // CKP-004-08: 钉住/置顶状态
    settings: {
      executionMode: 'local',
      hermesUrl: 'http://localhost:5000',
      contextMode: 'full',
      deployDir: 'D:\\SWAgentAddin\\frontend\\property-workbench',
      autoRefreshContext: false,    // SW 切换文档时自动刷新上下文
      treeViewMode: 'tree',         // 'tree' | 'flat'
      // RAG 配置
      ragProvider: 'hindsight',
      ragDbPath: 'D:\\SWAgentAddin\\rag\\materials.sqlite.db',
      ragCollection: 'davis',
      ragTopK: 5,
      ragScoreThreshold: 0.35
    }
  };

  var jobPollTimer = null;

  // ══════════════════════════════════════════════════════════
  //  工具函数
  // ══════════════════════════════════════════════════════════
  function esc(s) {
    s = (s == null ? '' : String(s));
    return s.replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function formatTime(dt) {
    if (!dt) return '';
    var d = parseDateValue(dt);
    if (!d) return String(dt);
    var pad = function (n) { return n < 10 ? '0' + n : n; };
    return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }

  function formatDateTime(dt) {
    var d = parseDateValue(dt);
    if (!d) return '-';
    var pad = function (n) { return n < 10 ? '0' + n : n; };
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + ' ' +
      pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
  }

  function parseDateValue(v) {
    if (v == null || v === '') return null;
    if (v instanceof Date) return isNaN(v.getTime()) ? null : v;
    var d = new Date(v);
    return isNaN(d.getTime()) ? null : d;
  }

  function formatDurationMs(ms) {
    if (ms == null || isNaN(ms) || ms < 0) return '-';
    ms = Math.round(ms);
    if (ms < 1000) return ms + 'ms';
    var sec = Math.floor(ms / 1000);
    if (sec < 60) return sec + 's';
    var min = Math.floor(sec / 60);
    sec = sec % 60;
    if (min < 60) return min + 'm' + (sec > 0 ? ' ' + sec + 's' : '');
    var hr = Math.floor(min / 60);
    min = min % 60;
    return hr + 'h' + (min > 0 ? ' ' + min + 'm' : '');
  }

  function pickTimestamp(obj) {
    obj = obj || {};
    var keys = ['completed_at', 'completedAt', 'finished_at', 'finishedAt',
      'started_at', 'startedAt', 'start_time', 'startTime',
      'submitted_at', 'submittedAt', 'created_at', 'createdAt', 'updated_at', 'updatedAt'];
    for (var i = 0; i < keys.length; i++) {
      var d = parseDateValue(obj[keys[i]]);
      if (d) return d;
    }
    return null;
  }

  function normalizeTaskTimes(source) {
    source = source || {};
    return {
      submitted_at: source.submitted_at || source.submittedAt || null,
      started_at: source.started_at || source.startedAt || null,
      completed_at: source.completed_at || source.completedAt || null,
      created_at: source.created_at || source.createdAt || null
    };
  }

  function syncJobTimestamps(job, data) {
    job = job || {};
    data = data || {};
    var submitted = parseDateValue(data.submitted_at || data.submittedAt || data.created_at || data.createdAt) ||
      parseDateValue(job.submitted_at);
    if (!job.submitted_at && submitted) job.submitted_at = submitted.toISOString();

    var started = parseDateValue(data.started_at || data.startedAt || data.start_time || data.startTime);
    if (started) {
      job.started_at = started.toISOString();
    } else if (!job.started_at && (job.status === 'running' || data.status === 'running')) {
      job.started_at = new Date().toISOString();
    }

    var completed = parseDateValue(data.completed_at || data.completedAt || data.finished_at || data.finishedAt || data.end_time || data.endTime);
    if (completed) {
      job.completed_at = completed.toISOString();
    } else if (!job.completed_at && isTerminalJobStatus(job.status || data.status)) {
      job.completed_at = new Date().toISOString();
    }
    return job;
  }

  function syncDraftTimestampsFromActiveJob() {
    var aj = state.activeJob;
    if (!aj) return;
    var changed = false;
    state.taskDrafts.forEach(function (d) {
      if (d.taskId !== state.currentTaskId && aj.job_id && d.submittedJobId !== aj.job_id) return;
      if (aj.submitted_at && d.submittedAt !== aj.submitted_at) { d.submittedAt = aj.submitted_at; changed = true; }
      if (aj.started_at && d.startedAt !== aj.started_at) { d.startedAt = aj.started_at; changed = true; }
      if (aj.completed_at && d.completedAt !== aj.completed_at) { d.completedAt = aj.completed_at; changed = true; }
    });
    if (changed) persistTaskDrafts();
  }

  function getTaskDurationText(times, status) {
    times = normalizeTaskTimes(times);
    var submit = parseDateValue(times.submitted_at || times.created_at);
    var start = parseDateValue(times.started_at) || submit;
    var end = parseDateValue(times.completed_at);
    if (start && end) return formatDurationMs(end.getTime() - start.getTime());
    if (start && (status === 'running' || status === 'submitting' || status === 'queued')) {
      return formatDurationMs(Date.now() - start.getTime()) + '…';
    }
    return '-';
  }

  function getTaskQueueTimeLabel(times, status) {
    times = normalizeTaskTimes(times);
    var t = times.completed_at || times.started_at || times.submitted_at || times.created_at;
    if (!t) return '-';
    return formatTime(t);
  }

  function buildTaskTimingDetailRows(times, status) {
    times = normalizeTaskTimes(times);
    var rows = [];
    if (times.created_at) rows.push(['创建时间', formatDateTime(times.created_at)]);
    rows.push(['提交时间', formatDateTime(times.submitted_at || times.created_at)]);
    rows.push(['开始时间', formatDateTime(times.started_at)]);
    rows.push(['完成时间', formatDateTime(times.completed_at)]);
    rows.push(['总耗时', getTaskDurationText(times, status)]);
    return rows;
  }

  function getEntryTimes(entry) {
    if (!entry) return normalizeTaskTimes({});
    if (entry.times) return normalizeTaskTimes(entry.times);
    if (entry.draft) {
      return normalizeTaskTimes({
        created_at: entry.draft.createdAt,
        submitted_at: entry.draft.submittedAt,
        started_at: entry.draft.startedAt,
        completed_at: entry.draft.completedAt
      });
    }
    if (entry.isActiveJob && state.activeJob) return normalizeTaskTimes(state.activeJob);
    return normalizeTaskTimes({});
  }

  function buildTaskTimeTitle(times, status) {
    return buildTaskTimingDetailRows(times, status).map(function (row) {
      return row[0] + ': ' + row[1];
    }).join('\n');
  }

  function getActiveJobItemCount(aj) {
    aj = aj || {};
    if (aj.total_items != null && Number(aj.total_items) > 0) return Number(aj.total_items);
    if (aj.instanceCount != null && Number(aj.instanceCount) > 0) return Number(aj.instanceCount);
    if (aj.reviewItems && aj.reviewItems.length) return aj.reviewItems.length;
    if (aj.summary && aj.summary.total != null) return Number(aj.summary.total);
    if (aj.payload && aj.payload.components && aj.payload.components.length) return aj.payload.components.length;
    return 0;
  }

  function getAgentReviewContext(aj) {
    aj = aj || {};
    var agent = aj.agentResult || {};
    return {
      executionMode: aj.executionMode || agent.execution_mode || '',
      summary: aj.summary || agent.summary || null,
      items: aj.reviewItems || agent.items || []
    };
  }

  function buildAgentReviewDetailRows(aj) {
    var ctx = getAgentReviewContext(aj);
    var rows = [];
    if (ctx.executionMode) rows.push(['执行模式', ctx.executionMode]);
    var summary = ctx.summary;
    if (summary) {
      rows.push(['审核汇总', '共 ' + (summary.total != null ? summary.total : ctx.items.length) +
        ' / 通过 ' + (summary.pass != null ? summary.pass : 0) +
        ' / 待修 ' + (summary.fix != null ? summary.fix : 0) +
        ' / 跳过 ' + (summary.skip != null ? summary.skip : 0) +
        ' / 失败 ' + (summary.fail != null ? summary.fail : 0)]);
    }
    if (ctx.items && ctx.items.length) {
      var reasonCounts = {};
      ctx.items.forEach(function (it) {
        var d = String((it && it.decision) || 'unknown');
        var r = String((it && it.reason) || d);
        reasonCounts[d + ' · ' + r] = (reasonCounts[d + ' · ' + r] || 0) + 1;
      });
      var reasonParts = Object.keys(reasonCounts).map(function (k) {
        return k + ' ×' + reasonCounts[k];
      });
      if (reasonParts.length) rows.push(['跳过/决策原因', reasonParts.join('；')]);
    }
    if (ctx.executionMode === 'mcp') {
      rows.push(['MCP 说明', 'Hermes 已完成计划生成；当前为 dry-run 预演，未授权前不会调用 MCP 写 PDM']);
    }
    return rows;
  }

  function countNodes(node) {
    if (!node) return 0;
    var n = 1;
    if (node.children) {
      for (var i = 0; i < node.children.length; i++) n += countNodes(node.children[i]);
    }
    return n;
  }

  function countParts(node) {
    if (!node) return 0;
    var n = isPartNode(node) ? 1 : 0;
    if (node.children) {
      for (var i = 0; i < node.children.length; i++) n += countParts(node.children[i]);
    }
    return n;
  }

  function findNodeById(tree, id) {
    if (!tree) return null;
    if (tree.id === id) return tree;
    if (tree.children) {
      for (var i = 0; i < tree.children.length; i++) {
        var found = findNodeById(tree.children[i], id);
        if (found) return found;
      }
    }
    return null;
  }

  // CKP-004-07: 按 displayName 查找节点（用于 SW 切换文档后恢复选中节点）
  function findNodeByDisplayName(tree, name) {
    if (!tree || !name) return null;
    if (tree.name === name) return tree;
    if (tree.children) {
      for (var i = 0; i < tree.children.length; i++) {
        var found = findNodeByDisplayName(tree.children[i], name);
        if (found) return found;
      }
    }
    return null;
  }

  function isPartNode(node) {
    if (!node) return false;
    if (isAssemblyNode(node)) return false;
    return node.type === 'part' || node.type === '零件' || node.docType === '零件' || node.isPart === true;
  }

  function isAssemblyNode(node) {
    if (!node) return false;
    return node.type === 'assembly' || node.type === '装配体' || node.docType === '装配体' || node.isAssembly === true;
  }

  function isNodeInPdmVault(node) {
    if (!node) return false;
    return node.isInPdmVault === true || node.IsInPdmVault === true;
  }

  function getNodeIconHtml(node) {
    return isAssemblyNode(node) ? ICONS.assembly : ICONS.part;
  }

  function getNodeIconClass(node) {
    return 'tree-icon' + (isNodeInPdmVault(node) ? ' tree-icon-pdm' : '');
  }

  function normalizeTreeDocType(node, row) {
    node = node || {};
    row = row || {};
    if (node.IsAssembly || node.isAssembly) return 'assembly';
    var raw = node.DocType || node.docType || row.DocType || row.docType || '';
    if (raw === '装配体') return 'assembly';
    if (raw === '零件') return 'part';
    return raw || 'part';
  }

  function normalizeFilePath(fp) {
    if (!fp) return '';
    fp = String(fp).trim();
    if (fp.indexOf('|') >= 0) return '';
    if (fp === '不可用' || fp === 'N/A' || fp === '') return '';
    return fp.replace(/\//g, '\\');
  }

  function extractPivotFilePart(pivotKey) {
    if (!pivotKey) return '';
    var parts = String(pivotKey).split('|');
    for (var i = parts.length - 1; i >= 0; i--) {
      var seg = (parts[i] || '').trim();
      if (!seg) continue;
      if (/\.(sldprt|sldasm|slddrw)$/i.test(seg) || seg.indexOf('\\') >= 0 || seg.indexOf('/') >= 0) {
        return normalizeFilePath(seg);
      }
    }
    return (parts[parts.length - 1] || '').trim();
  }

  function isUnkeyedGroupKey(groupKey) {
    return !groupKey || groupKey.indexOf('unkeyed:') === 0;
  }

  function isSubmittableNode(node) {
    if (!node || !isPartNode(node)) return false;
    // 装配体不能被添加到属性工作区
    if (isAssemblyNode(node)) return false;
    // 应用筛选器
    if (!isNodePassingFilters(node)) return false;
    return true;
  }

  // 筛选器检查
  function isNodePassingFilters(node) {
    if (!node) return true;
    var f = state.treeFilters;
    if (node.isLightweight && !f.lightweight) return false;
    if (node.isSuppressed && !f.suppressed) return false;
    if (node.isHidden && !f.hidden) return false;
    if (node.isEnvelope && !f.envelope) return false;
    if (node.isVirtual && !f.virtualComp) return false;
    if (node.isReadOnly && !f.readOnly) return false;
    return true;
  }

  // 构建时节点可见性（统一由筛选器管理）
  function isNodeVisible(node) {
    return isNodePassingFilters(node);
  }

  // CKP-004-18: 统一提取组件显示名
  // 从 C# CockpitTreeNode / PropertyRow 中提取干净的组件显示名
  // 永远不返回完整路径或 PivotKey
  function getCleanDisplayName(node, row) {
    node = node || {};
    row = row || {};
    // 1. 首选 DisplayName / Name / DocumentName（SW 组件名，如 "Bracket-1"）
    var raw = node.DisplayName || node.displayName || node.Name
           || node.DocumentName || node.documentName
           || row.DisplayName || row.displayName || row.LocalComponentName || row.localComponentName || '';
    // 已 normalize 的 node.name 仅作末位回退，避免误用 pivotKey 类字符串
    if (!raw) raw = node.name || '';
    if (raw) {
      // 如果 raw 包含 "|", 取最后一段（PivotKey fallthrough）
      if (raw.indexOf('|') >= 0) raw = raw.split('|').pop();
      // 去掉文件扩展名
      raw = raw.replace(/\.(SLDPRT|SLDASM|SLDDRW|sldprt|sldasm|slddrw)$/i, '');
      // 如果是纯路径，取最后一段文件名
      raw = raw.replace(/^.*[\\\/]/, '');
      // 去掉配置标记
      raw = raw.replace(/\s*\(默认\)\s*/g, '').trim();
      if (raw) return raw;
    }
    // 2. 如果 ComponentName 不同（SW 原始组件名如 "Bolt-1"）
    var compName = node.ComponentName || node.componentName || '';
    if (compName && compName.indexOf('|') < 0) {
      compName = compName.replace(/^.*[\\\/]/, '').replace(/\.(SLDPRT|SLDASM|SLDDRW|sldprt|sldasm|slddrw)$/i, '');
      var cleaned = compName.replace(/\s*\(默认\)\s*/g, '').trim();
      if (cleaned) return cleaned;
    }
    // 3. 最后回退：从 filePath 提取文件名
    var fp = node.FilePath || node.filePath || row.FilePath || row.filePath || '';
    if (fp && fp.indexOf('|') < 0) {
      var fn = fp.replace(/^.*[\\\/]/, '').replace(/\.(SLDPRT|SLDASM|SLDDRW|sldprt|sldasm|slddrw)$/i, '');
      if (fn && fn.indexOf(':') < 0) return fn;
    }
    // 4. 绝望回退
    return '(unnamed)';
  }

  // CKP-004-18: 树状/扁平名称清洗
  // 树状 stripInstance=false 保留实例后缀 "-数字"
  // 扁平 stripInstance=true  去掉实例后缀
  function cleanNodeName(name, stripInstance) {
    if (!name) return '';
    var cleaned = name;
    // 去掉完整路径中 "|" 分隔的 PivotKey
    if (cleaned.indexOf('|') >= 0) cleaned = cleaned.split('|').pop();
    // 去掉文件扩展名
    cleaned = cleaned.replace(/\.(SLDPRT|SLDASM|SLDDRW|sldprt|sldasm|slddrw)$/i, '');
    // 如果是路径，取文件名
    cleaned = cleaned.replace(/^.*[\\\/]/, '');
    // 去掉配置标记
    cleaned = cleaned.replace(/\s*\(默认\)\s*/g, '').trim();
    // 扁平视图去掉实例后缀 "-数字"
    if (stripInstance) cleaned = cleaned.replace(/-\d+$/, '');
    return cleaned || name;
  }

  // ── 设计树筛选栏 ──
  function renderTreeFilterBar() {
    var bar = document.getElementById('tree-filter-bar');
    if (!bar) return;
    var f = state.treeFilters;
    var btns = [
      { key: 'lightweight', label: '轻化', icon: '🪶' },
      { key: 'hidden',      label: '隐藏', icon: '👁' },
      { key: 'suppressed',  label: '压缩', icon: '📦' },
      { key: 'envelope',    label: '封套', icon: '✉️' },
      { key: 'virtualComp', label: '虚拟', icon: '💻' },
      { key: 'readOnly',    label: '只读', icon: '🔒' }
    ];
    bar.innerHTML = btns.map(function (b) {
      return '<button class="tree-filter-btn' + (f[b.key] ? ' active' : '') + '" data-filter="' + b.key + '">' +
        b.icon + ' ' + b.label + '</button>';
    }).join('');
    bar.querySelectorAll('.tree-filter-btn').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var key = this.getAttribute('data-filter');
        if (!key || !(key in state.treeFilters)) return;
        state.treeFilters[key] = !state.treeFilters[key];
        refreshDesignTree();
        renderTreeFilterBar();
        renderPropertyWorkbench();
        renderActionBar();
        renderStatusbar();
      });
    });
  }

  function clearFilteredCheckedIds() {
    var tree = state.context && state.context.tree;
    if (!tree) return;
    var newSet = new Set();
    state.checkedNodeIds.forEach(function (id) {
      var node = findNodeById(tree, id);
      if (node && isSubmittableNode(node)) newSet.add(id);
    });
    state.checkedNodeIds = newSet;
  }

  function filterVisibleChildren(children) {
    if (!children) return [];
    return children.filter(function (c) { return isNodeVisible(c); });
  }

  function isDefaultCheckablePart(node) {
    // 统一由 isSubmittableNode → isNodePassingFilters 管理所有筛选规则
    return isSubmittableNode(node);
  }

  // CKP-005-06: 节点分组 key（扁平/树状勾选联动，禁止 name-only 全局 key）
  function getNodeGroupKey(node) {
    if (!node) return 'unkeyed:unknown';

    var fp = normalizeFilePath(node.filePath);
    if (fp) return 'fp:' + fp.toLowerCase();

    var docId = node.documentKey || node.documentId || node.fileId || node.modelPath || '';
    docId = String(docId || '').trim();
    if (docId && docId.indexOf('|') < 0) return 'doc:' + docId.toLowerCase();

    // 完整 pivotKey（含 config + compName），避免仅末段 compName 导致全局合并
    var pivot = String(node.pivotKey || node.PivotKey || '').trim();
    if (pivot) return 'pivot:' + pivot.toLowerCase();

    // 装配树位置路径（C# InstancePath），轻化/虚拟件无 filePath 时仍唯一
    var inst = String(node.instancePath || node.InstancePath || node.parentPath || '').trim();
    if (inst) return 'inst:' + inst.toLowerCase();

    return 'unkeyed:' + (node.id || 'unknown');
  }

  // CKP-004-09: 查找同组所有节点
  function findNodesByGroupKey(tree, groupKey) {
    var result = [];
    if (!tree) return result;
    walkSubtree(tree, function (n) {
      if (getNodeGroupKey(n) === groupKey) result.push(n);
    });
    return result;
  }

  // CKP-005-06: 切换节点组勾选（仅 submittable；unkeyed 仅影响单节点）
  function applyNodeGroupChecked(groupKey, checked) {
    var tree = state.context && state.context.tree;
    if (!tree) return;
    if (isUnkeyedGroupKey(groupKey)) {
      var nodeId = groupKey.replace(/^unkeyed:/, '');
      var single = findNodeById(tree, nodeId);
      if (single && isSubmittableNode(single)) {
        if (checked) state.checkedNodeIds.add(single.id);
        else state.checkedNodeIds.delete(single.id);
      }
      return;
    }
    findNodesByGroupKey(tree, groupKey).forEach(function (n) {
      if (!isSubmittableNode(n)) return;
      if (checked) state.checkedNodeIds.add(n.id);
      else state.checkedNodeIds.delete(n.id);
    });
  }

  function toggleNodeGroupChecked(groupKey, checked) {
    applyNodeGroupChecked(groupKey, checked);
    refreshCheckedUi();
  }

  // CKP-004-19 Bug 2: 全选/取消所有零部件
  function setAllPartsChecked(checked) {
    var tree = state.context && state.context.tree;
    if (!tree) return;
    walkSubtree(tree, function (n) {
      if (isSubmittableNode(n)) {
        if (checked) state.checkedNodeIds.add(n.id);
        else state.checkedNodeIds.delete(n.id);
      }
    });
  }

  // CKP-005-06: 检查组是否全选（仅统计可提交零件）
  function isGroupFullyChecked(groupKey) {
    var tree = state.context && state.context.tree;
    if (!tree) return false;
    if (isUnkeyedGroupKey(groupKey)) {
      var nodeId = groupKey.replace(/^unkeyed:/, '');
      var single = findNodeById(tree, nodeId);
      return !!(single && isSubmittableNode(single) && state.checkedNodeIds.has(single.id));
    }
    var nodes = findNodesByGroupKey(tree, groupKey).filter(function (n) { return isSubmittableNode(n); });
    if (nodes.length === 0) return false;
    return nodes.every(function (n) { return state.checkedNodeIds.has(n.id); });
  }

  function isGroupPartiallyChecked(groupKey) {
    if (isGroupFullyChecked(groupKey)) return false;
    var tree = state.context && state.context.tree;
    if (!tree) return false;
    var nodes = findNodesByGroupKey(tree, groupKey).filter(function (n) { return isSubmittableNode(n); });
    return nodes.some(function (n) { return state.checkedNodeIds.has(n.id); });
  }

  function walkSubtree(node, fn) {
    if (!node) return;
    fn(node);
    (node.children || []).forEach(function (child) { walkSubtree(child, fn); });
  }

  function collectPartNodeIds(node, out) {
    out = out || [];
    if (!node) return out;
    walkSubtree(node, function (n) {
      if (isSubmittableNode(n)) out.push(n.id);
    });
    return out;
  }

  function initDefaultCheckedNodeIds() {
    state.checkedNodeIds = new Set();
    var tree = state.context && state.context.tree;
    if (!tree) return;
    walkSubtree(tree, function (n) {
      if (isDefaultCheckablePart(n)) state.checkedNodeIds.add(n.id);
    });
  }

  function getCheckedPartCount() {
    var tree = state.context && state.context.tree;
    if (!tree) return 0;
    var count = 0;
    state.checkedNodeIds.forEach(function (id) {
      var node = findNodeById(tree, id);
      if (node && isSubmittableNode(node)) count++;
    });
    return count;
  }

  function getCheckedNodeCount() {
    return state.checkedNodeIds.size;
  }

  function getAssemblyCheckState(node) {
    var partIds = collectPartNodeIds(node, []);
    if (partIds.length === 0) return { checked: false, indeterminate: false };
    var checkedCount = 0;
    partIds.forEach(function (id) {
      if (state.checkedNodeIds.has(id)) checkedCount++;
    });
    if (checkedCount === 0) return { checked: false, indeterminate: false };
    if (checkedCount === partIds.length) return { checked: true, indeterminate: false };
    return { checked: false, indeterminate: true };
  }

  function setSubtreeChecked(node, checked) {
    walkSubtree(node, function (n) {
      if (isSubmittableNode(n)) {
        if (checked) state.checkedNodeIds.add(n.id);
        else state.checkedNodeIds.delete(n.id);
      }
    });
  }

  function handleNodeCheckToggle(node, checked) {
    if (isAssemblyNode(node) && node.children && node.children.length > 0) {
      setSubtreeChecked(node, checked);
    } else if (isSubmittableNode(node)) {
      applyNodeGroupChecked(getNodeGroupKey(node), checked);
    } else if (isPartNode(node)) {
      if (checked) state.checkedNodeIds.add(node.id);
      else state.checkedNodeIds.delete(node.id);
    }
    refreshCheckedUi();
  }

  function refreshCheckedUi() {
    refreshDesignTree();
    updateOverviewSummary();
    updateOverviewBottomStats();
    renderActionBar();   // CKP-004-09: top action bar
    renderStatusbar();
  }

  function buildComponentFromNode(node) {
    return {
      component_id: node.id,
      component_path: node.filePath || '',
      file_path: node.filePath || '',
      name: node.name,
      type: node.type,
      doc_type: node.docType || node.type || '',
      quantity: node.quantity || 1,
      properties: node.properties || {},
      is_filtered_clean: !(node.isSuppressed || node.isLightweight),
      state: '未知',
      part_number: node.partNumber || node.part_number || node.name || '',
      configuration: node.configuration || '',
      operation: 'material_properties_review'
    };
  }

  function getCheckedComponents() {
    return getWorkspaceItems().map(function (item) {
      return buildComponentFromNode(item.node);
    });
  }

  // CKP-005-06: 属性工作区唯一数据源（按 group 去重，筛选仅影响可见性）
  function getWorkspaceItems() {
    var tree = state.context && state.context.tree;
    if (!tree) return [];
    var groups = {};
    var order = [];
    state.checkedNodeIds.forEach(function (id) {
      var node = findNodeById(tree, id);
      if (!node || !isSubmittableNode(node)) return;
      if (!isNodePassingFilters(node)) return;
      var gk = getNodeGroupKey(node);
      if (!groups[gk]) {
        groups[gk] = {
          groupKey: gk,
          node: node,
          instanceCount: 0,
          nodeIds: []
        };
        order.push(gk);
      }
      groups[gk].instanceCount++;
      groups[gk].nodeIds.push(id);
    });
    return order.map(function (gk) { return groups[gk]; });
  }

  function extractResultItems(result) {
    var data = (result && result.data) || {};
    var candidates = [
      data.items,
      data.results,
      data.data && data.data.items,
      data.data && data.data.results,
      data.data && data.data.data && data.data.data.items,
      data.data && data.data.data && data.data.data.results
    ];
    for (var i = 0; i < candidates.length; i++) {
      if (Array.isArray(candidates[i])) return candidates[i];
    }
    return [];
  }

  function isUsableFilePath(fp) {
    fp = String(fp || '').trim();
    return fp && fp !== '不可用' && fp !== 'N/A' && fp.indexOf('|') < 0;
  }

  function mergeNodeFilePath(node, row) {
    node = node || {};
    row = row || {};
    var rowFp = row.FilePath || row.filePath || '';
    var nodeFp = node.FilePath || node.filePath || '';
    if (isUsableFilePath(rowFp)) return rowFp;
    if (isUsableFilePath(nodeFp)) return nodeFp;
    return rowFp || nodeFp || '';
  }

  function applyContextFromResult(data, toastMessage) {
    if (!data) return false;
    var raw = data.context_json || data.contextJson || data.context;
    if (!raw) return false;
    window.MechPilot.receiveContext(raw);
    if (toastMessage) showToast(toastMessage);
    return true;
  }

  function hasActiveDocumentContext() {
    if (!state.context || !state.context.tree) return false;
    var name = state.context.fileName || '';
    return name && name !== '(none)' && name !== '无激活文档';
  }

  function isCommandSuccess(result) {
    return !!(result && (result.success === true || result.ok === true));
  }

  function getResultData(result) {
    return (result && result.data) || {};
  }

  function isTerminalJobStatus(status) {
    status = String(status || '').toLowerCase();
    return status === 'completed' || status === 'failed' || status === 'partial_failed' || status === 'cancelled' || status === 'canceled';
  }

  function isLocalReviewInProgress(job) {
    if (!job || !job.localReview) return false;
    var lr = job.localReview;
    if (lr.pending) return true;
    return lr.index < (lr.items ? lr.items.length : 0);
  }

  var localReviewTimeout = null;

  function clearLocalReviewTimeout() {
    if (localReviewTimeout) {
      clearTimeout(localReviewTimeout);
      localReviewTimeout = null;
    }
  }

  function armLocalReviewTimeout() {
    clearLocalReviewTimeout();
    localReviewTimeout = setTimeout(function () {
      var job = state.activeJob;
      var lr = job && job.localReview;
      if (!lr || !lr.pending) return;
      var item = lr.pending.item || {};
      finishLocalReviewItem({
        file_path: item.file_path,
        display_name: item.display_name || item.file_name || item.file_path,
        status: 'failed',
        stage: lr.pending.step || 'status',
        error: '本地 PDM 操作超时（45s）'
      });
      addAIMessage('system', '本地审核超时：' + (item.display_name || item.file_path || '未知对象'));
    }, 45000);
  }

  function findNodeByLooseName(tree, name) {
    if (!tree || !name) return null;
    var target = cleanNodeName(name, false).toLowerCase();
    var base = target.replace(/-\d+$/, '');
    function walk(n) {
      var nn = cleanNodeName(n.name || n.displayName || '', false).toLowerCase();
      if (nn === target || nn === base || (base && nn.indexOf(base) === 0)) return n;
      if (n.children) {
        for (var i = 0; i < n.children.length; i++) {
          var found = walk(n.children[i]);
          if (found) return found;
        }
      }
      return null;
    }
    return walk(tree);
  }

  function resolveAgentItemFilePath(item) {
    item = item || {};
    var fp = normalizeFilePath(item.file_path || item.filePath || item.path || '');
    if (isUsableFilePath(fp)) return fp;

    var labels = [
      item.display_name, item.displayName,
      item.file_name, item.fileName,
      item.name, item.component, item.component_id
    ].map(function (s) { return cleanNodeName(s || '', false); }).filter(Boolean);

    var tree = state.context && state.context.tree;
    if (tree) {
      for (var i = 0; i < labels.length; i++) {
        var node = findNodeByDisplayName(tree, labels[i]) || findNodeByLooseName(tree, labels[i]);
        if (node) {
          fp = normalizeFilePath(node.filePath || '');
          if (isUsableFilePath(fp)) return fp;
        }
      }
    }

    var table = state.context && (state.context.propertyTable || state.context.PropertyTable);
    var rows = table && (table.rows || table.Rows) || [];
    for (var r = 0; r < rows.length; r++) {
      var row = rows[r];
      var rowName = cleanNodeName(
        row.DisplayName || row.displayName || row.LocalComponentName || row.localComponentName || '',
        false
      );
      for (var j = 0; j < labels.length; j++) {
        if (!labels[j]) continue;
        if (rowName === labels[j] || rowName.indexOf(labels[j]) >= 0 || labels[j].indexOf(rowName) >= 0) {
          fp = normalizeFilePath(row.FilePath || row.filePath || '');
          if (isUsableFilePath(fp)) return fp;
        }
      }
    }
    return '';
  }

  function findLocalReviewJob(requestId) {
    if (!requestId) return null;
    if (state.activeJob && state.activeJob.localReview && state.activeJob.localReview.pending &&
        state.activeJob.localReview.pending.requestId === requestId) {
      return { job: state.activeJob, isActive: true };
    }
    for (var i = 0; i < state.submittedJobs.length; i++) {
      var job = state.submittedJobs[i];
      if (job.localReview && job.localReview.pending && job.localReview.pending.requestId === requestId) {
        return { job: job, isActive: false, index: i };
      }
    }
    return null;
  }

  function finalizeLocalReviewJob(job) {
    if (!job) return;
    clearLocalReviewTimeout();
    job.status = 'completed';
    job.current_stage = 'local_done';
    job.progress_percent = 100;
    if (!job.completed_at) job.completed_at = new Date().toISOString();
    job.message = 'Local review complete: ' + ((job.localReview && job.localReview.results) ? job.localReview.results.length : 0) + ' item(s)';
    job.localReview = null;
    if (state.activeJob && state.activeJob.job_id === job.job_id) state.activeJob = job;
    upsertSubmittedJob(job);
    syncDraftTimestampsFromActiveJob();
    refreshTaskList();
    renderJobStatusPanel();
    renderStatusbar();
  }

  function normalizeJobData(data, fallback) {
    data = data || {};
    fallback = fallback || {};
    var progress = data.progress_percent != null ? data.progress_percent : (data.progressPercent != null ? data.progressPercent : data.progress);
    progress = progress == null ? fallback.progress_percent : Number(progress);
    if (isNaN(progress)) progress = 0;
    progress = Math.max(0, Math.min(100, progress));

    return {
      job_id: data.job_id || data.jobId || data.id || fallback.job_id || '',
      accepted: data.accepted != null ? !!data.accepted : (fallback.accepted != null ? fallback.accepted : isCommandSuccess({ success: data.success, ok: data.ok })),
      status: data.status || fallback.status || (data.job_id || data.jobId ? 'queued' : 'unknown'),
      queue_position: data.queue_position != null ? data.queue_position : (data.queuePosition != null ? data.queuePosition : fallback.queue_position),
      estimated_wait_seconds: data.estimated_wait_seconds != null ? data.estimated_wait_seconds : (data.estimatedWaitSeconds != null ? data.estimatedWaitSeconds : fallback.estimated_wait_seconds),
      total_items: data.total_items != null ? data.total_items : (data.totalItems != null ? data.totalItems : fallback.total_items),
      completed_items: data.completed_items != null ? data.completed_items : (data.completedItems != null ? data.completedItems : fallback.completed_items),
      failed_items: data.failed_items != null ? data.failed_items : (data.failedItems != null ? data.failedItems : fallback.failed_items),
      progress_percent: progress,
      current_stage: data.current_stage || data.currentStage || fallback.current_stage || '',
      message: data.message || fallback.message || '',
      results: data.results || fallback.results || null,
      source: data.source || fallback.source || '',
      chat_message: data.chat_message || fallback.chat_message || '',
      output: data.output || fallback.output || '',
      submitted_at: fallback.submitted_at || data.submitted_at || data.submittedAt || data.created_at || data.createdAt || null,
      started_at: fallback.started_at || data.started_at || data.startedAt || data.start_time || data.startTime || null,
      completed_at: fallback.completed_at || data.completed_at || data.completedAt || data.finished_at || data.finishedAt || null,
      payload: fallback.payload || data.payload || null,
      request_id: fallback.request_id || data.request_id || data.requestId || null
    };
  }



  // DEMO-005-01 hotfix: parse structured MechPilot Agent outputs from Hermes poll wrappers.
  function canonicalizeAgentOutput(raw) {
    if (!raw) return null;
    if (typeof raw === 'string') {
      try { return canonicalizeAgentOutput(JSON.parse(raw)); } catch (e) { return null; }
    }
    if (Array.isArray(raw)) {
      for (var i = 0; i < raw.length; i++) {
        var fromArray = canonicalizeAgentOutput(raw[i]);
        if (fromArray) return fromArray;
      }
      return null;
    }
    if (typeof raw !== 'object') return null;
    if (raw.execution_mode || raw.executionMode || raw.items || raw.components || raw.pdm_batch_write_properties) {
      return applyAgentFieldAliases(raw);
    }
    var wrappers = [raw.results, raw.output, raw.result, raw.data, raw.message];
    for (var w = 0; w < wrappers.length; w++) {
      var parsed = canonicalizeAgentOutput(wrappers[w]);
      if (parsed) return parsed;
    }
    return null;
  }

  function applyAgentFieldAliases(raw) {
    var output = raw || {};
    var items = output.items || output.components || [];
    if (!Array.isArray(items)) items = [];
    items = items.map(function (item) {
      item = item || {};
      var props = item.properties || item.current_properties || item.currentProperties || {};
      var expected = item.expected_properties || item.expectedProperties || item.target_properties || item.targetProperties || {};
      return Object.assign({}, item, {
        file_path: item.file_path || item.filePath || item.path || '',
        file_name: item.file_name || item.fileName || item.name || '',
        configuration: item.configuration || item.config || item.config_name || '',
        properties: props,
        expected_properties: expected,
        decision: item.decision || item.action || item.status || ''
      });
    });
    var executionMode = output.execution_mode || output.executionMode;
    if (!executionMode && items.length >= 10) executionMode = 'mcp';
    if (!executionMode && items.length > 0) executionMode = 'local';
    return Object.assign({}, output, {
      execution_mode: executionMode,
      task_id: output.task_id || output.taskId || output.id || '',
      instance_count: output.instance_count != null ? output.instance_count : (output.instanceCount != null ? output.instanceCount : items.length),
      items: items,
      summary: output.summary || null
    });
  }

  function handleStructuredAgentCompletion(data, jobOverride) {
    data = enrichPollDataFromRaw(data || {});
    var pollJobId = (data && (data.job_id || data.jobId || data.id)) || '';
    var job = jobOverride || (pollJobId ? getJobById(pollJobId) : null) || state.activeJob;
    if (!job) return false;

    var raw = data.results || data.structured_result || data.output || data.result || data.data || data.message;
    var agent = canonicalizeAgentOutput(raw);
    if (!agent || !agent.execution_mode) return false;

    job.agentResult = agent;
    job.executionMode = agent.execution_mode;
    job.reviewItems = agent.items || [];
    job.instanceCount = agent.instance_count || (agent.items ? agent.items.length : 0);
    job.total_items = job.instanceCount;
    if (agent.summary) job.summary = agent.summary;
    if (agent.pdm_batch_write_properties) job.mcpRequest = agent.pdm_batch_write_properties;

    var summary = agent.summary || {};
    var counts = [];
    ['total', 'pass', 'fix', 'skip', 'fail'].forEach(function (key) {
      if (summary[key] != null) counts.push(key + '=' + summary[key]);
    });
    var countText = counts.length ? ' (' + counts.join(', ') + ')' : '';
    var isCurrentActive = state.activeJob && job.job_id && state.activeJob.job_id === job.job_id;

    if (agent.execution_mode === 'local') {
      job.hermesStatus = job.status || 'completed';
      job.status = 'local_running';
      job.completed_at = null;
      job.current_stage = 'local_pending';
      job.message = 'Agent local plan received: ' + job.reviewItems.length + ' item(s)' + countText;
      addAIMessage('system', job.message);
      if (isCurrentActive && typeof executeLocalMaterialReview === 'function') {
        state.activeJob = job;
        executeLocalMaterialReview(agent.items || []);
      } else if (!isCurrentActive) {
        addAIMessage('system', 'Local plan stored for job ' + (job.job_id || '') + ' (not the current active submit).');
      } else {
        addAIMessage('system', 'Local structured result received; local write executor is not loaded in this UI build. Check task details for expected_properties.');
      }
    } else if (agent.execution_mode === 'mcp') {
      job.current_stage = 'mcp_ready';
      job.message = 'Agent MCP batch plan received: ' + job.reviewItems.length + ' item(s)' + countText + '. Dry-run only before authorization.';
      addAIMessage('system', job.message);
    } else {
      job.message = 'Structured Agent result received: execution_mode=' + agent.execution_mode;
      addAIMessage('system', job.message);
    }

    if (isCurrentActive) state.activeJob = job;
    upsertSubmittedJob(job);
    refreshTaskList();
    renderJobStatusPanel();
    renderStatusbar();
    return true;
  }

  function applyFieldAliases(raw) {
    return applyAgentFieldAliases(raw);
  }

  function localStageLabel(stage) {
    var labels = {
      local_pending: '等待本地执行',
      local_setup: 'PDM 状态检查',
      local_checkout: 'PDM 检出',
      local_writing: '写入属性',
      local_saving: '保存确认',
      local_checkin: 'PDM 检入',
      local_done: '本地完成',
      local_skipped: '已跳过',
      local_running: '本地执行中',
      mcp_ready: 'MCP 批量就绪'
    };
    return labels[stage] || stage || '-';
  }

  function updateLocalReviewProgress(stage, message) {
    state.activeJob.current_stage = stage;
    state.activeJob.message = message || localStageLabel(stage);
    var lr = state.activeJob.localReview;
    if (lr && lr.items && lr.items.length) {
      state.activeJob.progress_percent = Math.min(99, Math.round((lr.index / lr.items.length) * 100));
    }
    refreshTaskList();
    renderJobStatusPanel();
    renderStatusbar();
  }

  function finishLocalReviewItem(record) {
    var lr = state.activeJob && state.activeJob.localReview;
    if (!lr) return;
    lr.results.push(record || {});
    lr.index += 1;
    lr.pending = null;
    clearLocalReviewTimeout();
    setTimeout(processLocalReviewItem, 0);
  }

  function processLocalReviewItem() {
    var lr = state.activeJob && state.activeJob.localReview;
    if (!lr || lr.index >= lr.items.length) {
      finalizeLocalReviewJob(state.activeJob);
      return;
    }
    var item = lr.items[lr.index];
    item.file_path = resolveAgentItemFilePath(item);
    var name = item.display_name || item.file_name || item.file_path || ('item-' + lr.index);
    var decision = String(item.decision || 'skip').toLowerCase();
    if (decision === 'pass' || decision === 'skip' || decision === 'fail') {
      finishLocalReviewItem({
        file_path: item.file_path,
        display_name: name,
        decision: decision,
        status: 'skipped',
        reason: item.reason || decision
      });
      return;
    }
    if (!isUsableFilePath(item.file_path)) {
      finishLocalReviewItem({
        file_path: item.file_path,
        display_name: name,
        decision: decision,
        status: 'blocked',
        error: '无法解析文件路径：' + name
      });
      return;
    }
    updateLocalReviewProgress('local_setup', localStageLabel('local_setup') + ': ' + name);
    var rid = window.MechPilot.sendCommand('local.pdm.status', { file_path: item.file_path });
    lr.pending = { requestId: rid, step: 'status', item: item };
    armLocalReviewTimeout();
  }

  function executeLocalMaterialReview(items) {
    items = items || [];
    if (!items.length) return;
    var dryRun = true;
    if (state.activeJob && state.activeJob.payload) {
      var sc = state.activeJob.payload.session_context || state.activeJob.payload;
      if (sc && sc.dry_run === false) dryRun = false;
    }
    state.activeJob.localReview = {
      items: items,
      index: 0,
      dryRun: dryRun,
      results: [],
      pending: null
    };
    processLocalReviewItem();
  }

  function handleLocalReviewResult(result) {
    var target = findLocalReviewJob(result.request_id);
    if (!target) {
      if (state.activeJob && state.activeJob.localReview && state.activeJob.localReview.pending) return false;
      return false;
    }
    var job = target.job;
    if (target.isActive) state.activeJob = job;
    else state.submittedJobs[target.index] = job;

    var lr = job.localReview;
    if (!lr || !lr.pending) return false;
    if (result.request_id && lr.pending.requestId && result.request_id !== lr.pending.requestId) return false;

    clearLocalReviewTimeout();

    var item = lr.pending.item;
    var step = lr.pending.step;
    var data = getResultData(result);
    var pdm = data.data || data;
    var name = item.display_name || item.file_path;

    if (step === 'status') {
      var st = String(pdm.status || 'error');
      if (st === 'not_in_vault' || st === 'checked_out_by_other' || st === 'error') {
        finishLocalReviewItem({
          file_path: item.file_path,
          display_name: name,
          decision: item.decision,
          status: 'blocked',
          pdm_status: st,
          error: pdm.error || st
        });
        return true;
      }
      if (lr.dryRun) {
        finishLocalReviewItem({
          file_path: item.file_path,
          display_name: name,
          decision: item.decision,
          status: 'dry_run_would_write',
          pdm_status: st,
          expected_properties: item.expected_properties || {}
        });
        return true;
      }
      updateLocalReviewProgress('local_checkout', localStageLabel('local_checkout') + ': ' + name);
      var ridCo = window.MechPilot.sendCommand('local.pdm.checkout', {
        file_path: item.file_path,
        comment: 'MechPilot property review'
      });
      lr.pending = { requestId: ridCo, step: 'checkout', item: item, pdm_status: st };
      armLocalReviewTimeout();
      return true;
    }
    if (step === 'checkout') {
      if (!isCommandSuccess(result) || pdm.success === false) {
        finishLocalReviewItem({
          file_path: item.file_path,
          display_name: name,
          status: 'failed',
          stage: 'checkout',
          error: pdm.error || 'checkout failed'
        });
        return true;
      }
      updateLocalReviewProgress('local_writing', localStageLabel('local_writing') + ': ' + name);
      var ridWr = window.MechPilot.sendCommand('local.material_review.write_properties', { items: [item] });
      lr.pending = { requestId: ridWr, step: 'writing', item: item };
      armLocalReviewTimeout();
      return true;
    }
    if (step === 'writing') {
      if (!isCommandSuccess(result)) {
        finishLocalReviewItem({
          file_path: item.file_path,
          display_name: name,
          status: 'failed',
          stage: 'writing',
          error: (result.error && result.error.message) || 'write failed'
        });
        return true;
      }
      updateLocalReviewProgress('local_checkin', localStageLabel('local_checkin') + ': ' + name);
      var ridCi = window.MechPilot.sendCommand('local.pdm.checkin', {
        file_path: item.file_path,
        comment: 'MechPilot property review'
      });
      lr.pending = { requestId: ridCi, step: 'checkin', item: item };
      armLocalReviewTimeout();
      return true;
    }
    if (step === 'checkin') {
      finishLocalReviewItem({
        file_path: item.file_path,
        display_name: name,
        status: isCommandSuccess(result) ? 'completed' : 'failed',
        stage: 'checkin',
        error: pdm.error || ''
      });
      return true;
    }
    return false;
  }

  function formatSeconds(seconds) {
    if (seconds == null || seconds === '') return '-';
    seconds = Number(seconds);
    if (isNaN(seconds)) return '-';
    if (seconds < 60) return Math.max(0, Math.round(seconds)) + ' 秒';
    return Math.floor(seconds / 60) + ' 分 ' + Math.round(seconds % 60) + ' 秒';
  }

  function buildJobPayload(intent, extra) {
    var c = state.context;
    var selected = state.selectedNode;
    var components = getCheckedComponents();
    var tree = c ? c.tree : null;
    var payload = {
      intent: intent || 'material_properties_review',
      session_context: {
        source: 'cockpit',
        operation: 'material_properties_review',
        dry_run: true,
        page: state.currentPage,
        assembly_path: c ? c.filePath : '',
        fileName: c ? c.fileName : '',
        filePath: c ? c.filePath : '',
        mode: c ? c.mode : state.settings.executionMode,
        view_mode: state.viewMode || 'tree',
        auto_fix_enabled: false,
        engineer_id: state.settings.engineerId || state.settings.engineer_id || '',
        total_selected: tree ? countParts(tree) : 0,
        selected_count: getCheckedPartCount(),
        submitted_at: new Date().toISOString(),
        selectedNode: selected ? {
          id: selected.id,
          name: selected.name,
          type: selected.type,
          filePath: selected.filePath
        } : null,
        checkedCount: getCheckedNodeCount(),
        checkedPartCount: getCheckedPartCount(),
        contextMode: state.settings.contextMode
      },
      components: components
    };
    Object.keys(extra || {}).forEach(function (key) { payload[key] = extra[key]; });
    return payload;
  }

  // ══════════════════════════════════════════════════════════
  //  localStorage 持久化 (CKP-004-05)
  // ══════════════════════════════════════════════════════════
  var LS_SNAPSHOTS = 'mechpilot.workspace.snapshots.v1';
  var LS_TASK_DRAFTS = 'mechpilot.workspace.taskDrafts.v1';
  var LS_SUBMITTED_JOBS = 'mechpilot.workspace.submittedJobs.v1';
  var LS_CURRENT_TASK = 'mechpilot.workspace.currentTaskId.v1';
  var MAX_SNAPSHOTS = 30;
  var MAX_SUBMITTED_JOBS = 100;

  function saveToLS(key, data) {
    try { localStorage.setItem(key, JSON.stringify(data)); } catch (e) { /* quota exceeded */ }
  }
  function loadFromLS(key) {
    try { var v = localStorage.getItem(key); return v ? JSON.parse(v) : null; } catch (e) { return null; }
  }

  function persistSnapshots() { saveToLS(LS_SNAPSHOTS, state.snapshots); }
  function persistTaskDrafts() { saveToLS(LS_TASK_DRAFTS, state.taskDrafts); }
  function persistSubmittedJobs() { saveToLS(LS_SUBMITTED_JOBS, state.submittedJobs); }
  function persistCurrentTask() { saveToLS(LS_CURRENT_TASK, state.currentTaskId); }

  function cloneJobRecord(job) {
    if (!job) return null;
    try { return JSON.parse(JSON.stringify(job)); } catch (e) { return null; }
  }

  function validateSubmittedJob(j) {
    if (!j || typeof j !== 'object') return false;
    var id = j.job_id || j.taskId;
    return typeof id === 'string' && id.length > 0 && id.length <= 200;
  }

  function findSubmittedJobIndex(jobId) {
    if (!jobId) return -1;
    for (var i = 0; i < state.submittedJobs.length; i++) {
      if (state.submittedJobs[i].job_id === jobId) return i;
    }
    return -1;
  }

  function getJobById(jobId) {
    if (!jobId) return null;
    if (state.activeJob && state.activeJob.job_id === jobId) return state.activeJob;
    var idx = findSubmittedJobIndex(jobId);
    return idx >= 0 ? state.submittedJobs[idx] : null;
  }

  function upsertSubmittedJob(job) {
    if (!job) return;
    var rec = cloneJobRecord(job);
    if (!rec) return;
    if (!rec.taskId) rec.taskId = rec.job_id || ('job-' + Date.now());
    if (!rec.job_id) rec.job_id = rec.taskId;
    var idx = findSubmittedJobIndex(rec.job_id);
    if (idx >= 0) state.submittedJobs[idx] = rec;
    else state.submittedJobs.unshift(rec);
    if (state.submittedJobs.length > MAX_SUBMITTED_JOBS) {
      state.submittedJobs.length = MAX_SUBMITTED_JOBS;
    }
    persistSubmittedJobs();
  }

  function archiveActiveJob() {
    if (!state.activeJob || !state.activeJob.job_id) return;
    upsertSubmittedJob(state.activeJob);
  }

  function syncActiveJobToHistory() {
    if (state.activeJob && state.activeJob.job_id) upsertSubmittedJob(state.activeJob);
  }

  function getTaskQueueSortTime(entry) {
    if (!entry) return 0;
    var t = entry.sortTime;
    if (t) {
      var preset = parseDateValue(t);
      if (preset) return preset.getTime();
    }
    var times = entry.times || {};
    var raw = times.submitted_at || times.created_at || times.completed_at || times.started_at;
    if (!raw && entry.draft) raw = entry.draft.submittedAt || entry.draft.createdAt;
    var d = parseDateValue(raw);
    return d ? d.getTime() : 0;
  }

  function sortTaskQueueEntries(entries) {
    entries.sort(function (a, b) { return getTaskQueueSortTime(b) - getTaskQueueSortTime(a); });
    return entries;
  }

  function getTaskQueueCount() {
    var seen = {};
    var count = state.taskDrafts.length;
    state.submittedJobs.forEach(function (j) {
      var id = j.job_id || j.taskId;
      if (id) seen[id] = true;
    });
    count += state.submittedJobs.length;
    if (state.activeJob) {
      var ajId = state.activeJob.job_id || state.activeJob._pendingId;
      if (ajId && !seen[ajId]) count += 1;
      else if (!ajId) count += 1;
    }
    return count;
  }

  function jobRecordToQueueEntry(job, opts) {
    opts = opts || {};
    var typeLabel = job.source || job.type || job.taskType || '任务';
    return {
      isDraft: false,
      isActiveJob: !!opts.isActiveJob,
      taskId: job.job_id || job.taskId || job._pendingId || ('job-' + Date.now()),
      title: typeLabel,
      taskType: job.source || job.type || '',
      typeLabel: typeLabel,
      typeIcon: opts.isActiveJob
        ? (job.status === 'running' ? '⚡' : (job.status === 'completed' ? '✅' : '⏳'))
        : '📤',
      objCount: getActiveJobItemCount(job),
      status: job.status || 'unknown',
      queuePos: '',
      submittedJobId: job.job_id || '',
      progress: job.progress_percent,
      completedItems: job.completed_items,
      totalItems: getActiveJobItemCount(job),
      currentStage: job.current_stage || '',
      jobMessage: job.message || '',
      results: job.results || null,
      reviewItems: job.reviewItems,
      summary: job.summary,
      executionMode: job.executionMode,
      sortTime: job.submitted_at || job.created_at || job.completed_at,
      times: {
        created_at: job.created_at,
        submitted_at: job.submitted_at,
        started_at: job.started_at,
        completed_at: job.completed_at
      },
      draft: null
    };
  }

  function getJobsNeedingPoll() {
    var seen = {};
    var out = [];
    function add(job) {
      if (!job || !job.job_id || isTerminalJobStatus(job.status)) return;
      if (job.status === 'local_running' || isLocalReviewInProgress(job)) return;
      if (seen[job.job_id]) return;
      seen[job.job_id] = true;
      out.push(job);
    }
    add(state.activeJob);
    state.submittedJobs.forEach(add);
    return out;
  }

  function restartJobPollingAll() {
    clearJobPollTimer();
    if (!window.MechPilot || typeof window.MechPilot.sendCommand !== 'function') return;
    var jobs = getJobsNeedingPoll();
    if (!jobs.length) return;
    jobs.forEach(function (j) {
      window.MechPilot.sendCommand('agent.job.poll', { job_id: j.job_id });
    });
    jobPollTimer = setInterval(function () {
      var pending = getJobsNeedingPoll();
      if (!pending.length) {
        clearJobPollTimer();
        return;
      }
      pending.forEach(function (j) {
        window.MechPilot.sendCommand('agent.job.poll', { job_id: j.job_id });
      });
    }, 3000);
  }

  function resolvePollTargetJob(data) {
    data = data || {};
    var jobId = data.job_id || data.jobId || data.id || '';
    if (state.activeJob) {
      if (!jobId) return { ref: state.activeJob, isActive: true };
      if (state.activeJob.job_id === jobId) return { ref: state.activeJob, isActive: true };
      if (!state.activeJob.job_id && state.activeJob.status === 'submitting') return { ref: state.activeJob, isActive: true };
    }
    var idx = findSubmittedJobIndex(jobId);
    if (idx >= 0) return { ref: state.submittedJobs[idx], isActive: false, index: idx };
    return null;
  }

  function loadPersistedState() {
    // CKP-004-23 P0-1.2: schema 白名单校验，拒绝畸形/注入数据
    var snaps = loadFromLS(LS_SNAPSHOTS);
    if (Array.isArray(snaps)) state.snapshots = snaps.filter(validateSnapshot);
    var drafts = loadFromLS(LS_TASK_DRAFTS);
    if (Array.isArray(drafts)) state.taskDrafts = drafts.filter(validateTaskDraft);
    var jobs = loadFromLS(LS_SUBMITTED_JOBS);
    if (Array.isArray(jobs)) state.submittedJobs = jobs.filter(validateSubmittedJob);
    var curTask = loadFromLS(LS_CURRENT_TASK);
    if (typeof curTask === 'string' && curTask.length > 0 && curTask.length <= 200) state.currentTaskId = curTask;
  }

  // CKP-004-23 P0-1.2: snapshot 模式校验
  function validateSnapshot(s) {
    if (!s || typeof s !== 'object') return false;
    if (typeof s.snapshotId !== 'string' || s.snapshotId.length > 200) return false;
    if (typeof s.createdAt !== 'string' || s.createdAt.length > 50) return false;
    if (s.reason && (typeof s.reason !== 'string' || s.reason.length > 200)) return false;
    // designTree 可为 null/object（不做内容排查），防止超大对象
    return true;
  }

  // CKP-004-23 P0-1.2: taskDraft 模式校验
  function validateTaskDraft(d) {
    if (!d || typeof d !== 'object') return false;
    if (typeof d.taskId !== 'string' || d.taskId.length > 200) return false;
    if (typeof d.taskType !== 'string' || d.taskType.length > 50) return false;
    if (!Array.isArray(d.selectedObjectIds)) return false;
    if (!Array.isArray(d.selectedObjectNames)) return false;
    if (typeof d.title !== 'string' || d.title.length > 500) return false;
    return true;
  }

  // ══════════════════════════════════════════════════════════
  //  快照管理 (工作流 A + B)
  // ══════════════════════════════════════════════════════════
  function createSnapshot(reason) {
    var ctx = state.context || {};
    var snap = {
      snapshotId: 'snap-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8),
      createdAt: new Date().toISOString(),
      reason: reason || 'manual',
      activeDocument: {
        fileName: ctx.fileName || '',
        filePath: ctx.filePath || '',
        docType: ctx.tree ? (ctx.tree.docType || '') : ''
      },
      designTree: ctx.tree ? JSON.parse(JSON.stringify(ctx.tree)) : null,
      selection: state.selectedNode ? { id: state.selectedNode.id, name: state.selectedNode.name } : null,
      selectionSummary: {
        checkedCount: getCheckedNodeCount(),
        checkedPartCount: getCheckedPartCount(),
        checkedNodeIds: Array.from(state.checkedNodeIds)
      },
      taskDrafts: JSON.parse(JSON.stringify(state.taskDrafts)),
      taskList: JSON.parse(JSON.stringify(state.submittedJobs.slice(0, 20))),
      aiThreads: state.aiThreads.slice(-10).map(function (t) { return { id: t.id, taskId: t.taskId, messages: t.messages.slice(-5) }; })
    };
    state.snapshots.push(snap);
    while (state.snapshots.length > MAX_SNAPSHOTS) state.snapshots.shift();
    persistSnapshots();
    return snap;
  }

  function renderSnapshotList() {
    if (state.snapshots.length === 0) {
      return '<div class="snapshot-empty">暂无快照。在任务编排页刷新上下文时会自动保存。</div>';
    }
    var html = '<div class="snapshot-list">';
    var recent = state.snapshots.slice(-3).reverse();
    recent.forEach(function (snap) {
      var docName = snap.activeDocument ? snap.activeDocument.fileName : '未知';
      var taskCount = snap.taskDrafts ? snap.taskDrafts.length : 0;
      var selCount = snap.selectionSummary ? snap.selectionSummary.checkedCount : 0;
      var time = snap.createdAt ? formatTime(snap.createdAt) : '';
      html += '<div class="snapshot-item" data-snapshot-id="' + esc(snap.snapshotId) + '">' +
        '<div class="snapshot-info">' +
          '<span class="snapshot-doc">' + esc(docName) + '</span>' +
          '<span class="snapshot-meta">' + time + ' · ' + taskCount + '任务 · ' + selCount + '选中</span>' +
        '</div>' +
        '<button class="snapshot-restore-btn" data-snapshot-id="' + esc(snap.snapshotId) + '">恢复</button>' +
      '</div>';
    });
    html += '</div>';
    return html;
  }

  function restoreSnapshot(snapshotId) {
    var snap = state.snapshots.find(function (s) { return s.snapshotId === snapshotId; });
    if (!snap) { showToast('快照不存在'); return; }

    // CKP-004-07: 记录快照恢复前的 SW 上下文状态
    var wasFromRealContext = state.context && !state.context._isMock;
    var oldFileName = state.context ? state.context.fileName : '';

    // Restore context
    if (snap.designTree) {
      state.context = state.context || {};
      state.context.tree = snap.designTree;
      state.context.fileName = snap.activeDocument ? snap.activeDocument.fileName : '';
      state.context.filePath = snap.activeDocument ? snap.activeDocument.filePath : '';
      // CKP-004-07: Mark as stale until verified
      state.context._isStaleSnapshot = true;
      state.context._snapshotSource = snap.snapshotId;
    }
    // Restore selection
    if (snap.selection && snap.designTree) {
      state.selectedNode = findNodeById(snap.designTree, snap.selection.id);
    }
    // Restore checked nodes
    if (snap.selectionSummary && snap.selectionSummary.checkedNodeIds) {
      state.checkedNodeIds = new Set(snap.selectionSummary.checkedNodeIds);
    }
    // Restore task drafts
    if (snap.taskDrafts) {
      state.taskDrafts = snap.taskDrafts;
      persistTaskDrafts();
    }
    // Restore tasks
    if (snap.taskList) {
      state.submittedJobs = snap.taskList;
      persistSubmittedJobs();
    }

    // Navigate to workspace
    navigatePage('workspace');
    showToast('⚠️ 已从快照恢复：' + (snap.activeDocument ? snap.activeDocument.fileName : '未知文档') + ' — 尚未重新校验 SolidWorks 当前状态，建议点击"刷新上下文"');
  }

  // ══════════════════════════════════════════════════════════
  //  任务草稿管理 (工作流 C)
  // ══════════════════════════════════════════════════════════
  var TASK_TYPES = [
    { type: 'property_review', label: '属性审核', icon: '📋' },
    { type: 'ai_analysis', label: 'AI 分析', icon: '🤖' },
    { type: 'pdm_status_check', label: 'PDM 状态检查', icon: '📦' }
  ];

  function createTaskDraft(taskType) {
    var typeInfo = TASK_TYPES.find(function (t) { return t.type === taskType; }) || TASK_TYPES[0];
    var checkedIds = Array.from(state.checkedNodeIds);
    var checkedNames = [];
    if (state.context && state.context.tree) {
      checkedIds.forEach(function (id) {
        var n = findNodeById(state.context.tree, id);
        if (n) checkedNames.push(n.name);
      });
    }

    // Create a snapshot for this task
    var snap = createSnapshot('task_draft');

    var draft = {
      taskId: 'task-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8),
      taskType: taskType,
      title: typeInfo.icon + ' ' + typeInfo.label + ' — ' + (checkedNames.length > 0 ? checkedNames.slice(0, 3).join(', ') + (checkedNames.length > 3 ? ' 等' + checkedNames.length + '项' : '') : '当前文档'),
      status: 'draft',
      snapshotId: snap.snapshotId,
      selectedObjectIds: checkedIds,
      selectedObjectNames: checkedNames,
      selectedObjectCount: checkedIds.length,
      createdAt: new Date().toISOString(),
      aiThreadId: 'thread-' + Date.now()
    };

    state.taskDrafts.push(draft);
    state.currentTaskId = draft.taskId;
    persistTaskDrafts();
    persistCurrentTask();

    // Ensure AI thread exists
    if (!state.aiThreads.find(function (t) { return t.id === draft.aiThreadId; })) {
      state.aiThreads.push({ id: draft.aiThreadId, taskId: draft.taskId, messages: [] });
    }

    showToast('已创建任务草稿：' + typeInfo.label + '（' + checkedIds.length + ' 个对象）');
    refreshTaskList();
    updateAIHeader();
  }

  function selectTask(taskId) {
    state.currentTaskId = taskId;
    persistCurrentTask();
    refreshTaskList();
    updateAIHeader();
  }

  function getCurrentTask() {
    if (!state.currentTaskId) return null;
    return state.taskDrafts.find(function (d) { return d.taskId === state.currentTaskId; }) || null;
  }

  // ══════════════════════════════════════════════════════════
  //  Toast 提示 (不用 alert)
  // ══════════════════════════════════════════════════════════
  var toastTimer = null;
  function showToast(msg, duration) {
    state.toastMessage = msg;
    var el = document.getElementById('toast-container');
    if (!el) {
      el = document.createElement('div');
      el.id = 'toast-container';
      el.className = 'toast-container';
      document.body.appendChild(el);
    }
    el.textContent = msg;
    el.classList.add('show');
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(function () {
      el.classList.remove('show');
      state.toastMessage = null;
    }, duration || 3000);
  }

  function clearJobPollTimer() {
    if (jobPollTimer) {
      clearInterval(jobPollTimer);
      jobPollTimer = null;
    }
  }

  function startJobPolling(jobId) {
    restartJobPollingAll();
  }

  function submitJob(command, payload, sourceLabel) {
    archiveActiveJob();
    clearJobPollTimer();
    state.activeJob = {
      job_id: '',
      _pendingId: 'pending-' + Date.now(),
      accepted: false,
      status: 'submitting',
      progress_percent: 0,
      current_stage: '提交中',
      source: sourceLabel || command,
      submitted_at: new Date().toISOString(),
      started_at: null,
      completed_at: null,
      request_id: null,
      payload: payload
    };
    renderJobStatusPanel();
    renderStatusbar();
    refreshTaskList();

    try {
      state.activeJob.request_id = window.MechPilot.sendCommand(command, payload);
      addAIMessage('system', '已提交任务，等待后端受理：' + (sourceLabel || command));
    } catch (e) {
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '提交失败';
      state.activeJob.message = '无法发送任务请求，请检查 WebView2 / Hermes 连接。';
      if (!state.activeJob.completed_at) state.activeJob.completed_at = new Date().toISOString();
      syncDraftTimestampsFromActiveJob();
      syncActiveJobToHistory();
      renderJobStatusPanel();
      renderStatusbar();
      refreshTaskList();
      addAIMessage('system', state.activeJob.message);
    }
  }

  function formatHermesFailureMessage(result) {
    var msg = (result && result.message) || (result && result.error && result.error.message) || 'Hermes 请求失败';
    if (/401|403|Unauthorized|未授权/i.test(msg)) {
      return msg + '。请检查 config.json 中 agent_server.api_key（auth_mode=bearer 时必填）。';
    }
    if (/404|Not Found/i.test(msg)) {
      return msg + '。请确认 agent_server 使用 /v1/runs（勿用 /api/jobs 或 MCP :19090）。';
    }
    return msg;
  }

  function handleJobSubmitResult(result) {
    var data = getResultData(result);
    if (!isCommandSuccess(result) || !(data.job_id || data.jobId || data.id)) {
      state.activeJob = normalizeJobData(data, state.activeJob);
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '受理失败';
      state.activeJob.message = formatHermesFailureMessage(result);
      if (!state.activeJob.completed_at) state.activeJob.completed_at = new Date().toISOString();
      syncDraftTimestampsFromActiveJob();
      syncActiveJobToHistory();
      clearJobPollTimer();
      renderJobStatusPanel();
      renderStatusbar();
      refreshTaskList();
      addAIMessage('system', state.activeJob.message);
      // CKP-004-10: 自动更新 Hermes 状态
      if (/401|403|Unauthorized|未授权/i.test(state.activeJob.message)) updateHermesStatus('auth_required', state.activeJob.message);
      else if (/failed|失败|timeout|超时|offline/i.test(state.activeJob.message)) updateHermesStatus('offline', state.activeJob.message);
      else updateHermesStatus('error', state.activeJob.message);
      return;
    }

    state.activeJob = normalizeJobData(data, state.activeJob);
    syncJobTimestamps(state.activeJob, data);
    state.activeJob.accepted = true;
    state.activeJob.status = state.activeJob.status || 'queued';
    state.activeJob.current_stage = state.activeJob.current_stage || '排队中';
    if (state.activeJob._pendingId) delete state.activeJob._pendingId;
    syncDraftTimestampsFromActiveJob();
    syncActiveJobToHistory();
    renderJobStatusPanel();
    renderStatusbar();
    refreshTaskList();
    addAIMessage('system', '任务已受理，Job ID：' + state.activeJob.job_id);
    restartJobPollingAll();
    // CKP-004-10: 成功提交 → 自动更新为 online
    updateHermesStatus('online', '任务提交成功，Job ID: ' + state.activeJob.job_id);
  }

  // CKP-004-10: 统一 Hermes 状态更新
  function updateHermesStatus(status, message) {
    state.hermesStatus.status = status;
    state.hermesStatus.message = message || '';
    state.hermesStatus.checked_at = new Date().toISOString();
    state.hermesOnline = status === 'online';
    // Refresh dashboard inline if on the dashboard page
    var card = document.querySelector('.dash-status .dash-card-body');
    if (card && state.currentPage === 'dashboard') {
      var rows = card.querySelectorAll('.status-row');
      if (rows.length >= 3) {
        rows[2].innerHTML =
          '<span class="status-dot ' + hermesStatusDot(status) + '"></span> Hermes ' +
          '<span class="status-val">' + hermesStatusLabel(status) + '</span>' +
          '<button class="dash-refresh-btn" id="btn-hermes-reconnect" title="重新检测 Hermes 连接">🔄 重新连接</button>';
        var newBtn = rows[2].querySelector('#btn-hermes-reconnect');
        if (newBtn) {
          newBtn.addEventListener('click', function () {
            state.hermesStatus.status = 'checking';
            state.hermesStatus.message = '';
            window.MechPilot.sendCommand('agent.health.check', {});
          });
        }
      }
    }
  }

  function handleChatDirectResult(result) {
    var data = getResultData(result);
    clearJobPollTimer();
    if (!state.activeJob) state.activeJob = { source: 'ai.assistant.chat' };
    state.activeJob.status = 'completed';
    state.activeJob.accepted = true;
    state.activeJob.current_stage = '已完成';
    state.activeJob.progress_percent = 100;
    if (!state.activeJob.completed_at) state.activeJob.completed_at = new Date().toISOString();
    syncDraftTimestampsFromActiveJob();
    syncActiveJobToHistory();
    refreshTaskList();
    renderJobStatusPanel();
    renderStatusbar();
    var chatReply = '';
    if (data && data.content) chatReply = data.content;
    else if (data && data.output) chatReply = data.output;
    else if (data && data.message) chatReply = data.message;
    else if (result && result.message) chatReply = result.message;
    if (!chatReply) chatReply = 'Agent 已响应（无文本内容）';
    addAIMessage('ai', chatReply);
  }

  function enrichPollDataFromRaw(data) {
    data = data || {};
    if (data.output || data.results || data.structured_result) return data;
    var raw = data.raw;
    if (!raw) return data;
    try {
      var parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
      if (parsed && typeof parsed === 'object') {
        if (parsed.output && !data.output) data.output = parsed.output;
        if (parsed.results && !data.results) data.results = parsed.results;
        if (parsed.structured_result && !data.structured_result) data.structured_result = parsed.structured_result;
        if (parsed.status && (!data.status || data.status === 'unknown')) data.status = parsed.status;
        if (parsed.error && !data.message) data.message = typeof parsed.error === 'string' ? parsed.error : JSON.stringify(parsed.error);
        if (parsed.started_at && !data.started_at) data.started_at = parsed.started_at;
        if (parsed.completed_at && !data.completed_at) data.completed_at = parsed.completed_at;
        if (parsed.submitted_at && !data.submitted_at) data.submitted_at = parsed.submitted_at;
        if (parsed.created_at && !data.created_at) data.created_at = parsed.created_at;
      }
    } catch (e) { /* ignore malformed raw */ }
    return data;
  }

  function handleJobPollResult(result) {
    var data = enrichPollDataFromRaw(getResultData(result));
    if (!isCommandSuccess(result)) {
      var failTarget = resolvePollTargetJob(data);
      if (!failTarget) {
        if (!state.activeJob) state.activeJob = {};
        failTarget = { ref: state.activeJob, isActive: true };
      }
      failTarget.ref.status = 'failed';
      failTarget.ref.current_stage = '轮询失败';
      failTarget.ref.message = (result && result.message) || (result && result.error && result.error.message) || '获取任务状态失败，请检查 Hermes 服务。';
      if (!failTarget.ref.completed_at) failTarget.ref.completed_at = new Date().toISOString();
      if (failTarget.isActive) state.activeJob = failTarget.ref;
      else state.submittedJobs[failTarget.index] = failTarget.ref;
      syncDraftTimestampsFromActiveJob();
      syncActiveJobToHistory();
      restartJobPollingAll();
      renderJobStatusPanel();
      renderStatusbar();
      refreshTaskList();
      addAIMessage('system', failTarget.ref.message);
      return;
    }

    var target = resolvePollTargetJob(data);
    if (!target) return;
    var merged = normalizeJobData(data, target.ref);
    syncJobTimestamps(merged, data);
    if (data.results) merged.results = data.results;
    if (target.isActive) state.activeJob = merged;
    else state.submittedJobs[target.index] = merged;
    if (state.activeJob && merged.job_id && state.activeJob.job_id === merged.job_id) {
      state.activeJob = merged;
    }
    syncDraftTimestampsFromActiveJob();
    syncActiveJobToHistory();
    renderJobStatusPanel();
    renderStatusbar();
    refreshTaskList();
    if (isTerminalJobStatus(merged.status)) {
      if (handleStructuredAgentCompletion(data, merged)) {
        restartJobPollingAll();
        return;
      }

      // For normal AI chat, show result as AI bubble
      if (merged.source === 'ai.assistant.chat') {
        if (merged.status === 'completed' || merged.status === 'partial_failed') {
          var chatReply = '';
          if (data.content) {
            chatReply = data.content;
          } else if (data.result && typeof data.result === 'object') {
            chatReply = data.result.content || data.result.output || data.result.message || '';
          } else if (data.result && typeof data.result === 'string') {
            chatReply = data.result;
          } else if (data.output) {
            chatReply = data.output;
          } else if (data.message) {
            chatReply = data.message;
          }
          if (merged.results && Array.isArray(merged.results) && merged.results.length > 0) {
            var first = merged.results[0];
            if (first.content) chatReply = first.content;
            else if (first.output) chatReply = first.output;
            else if (first.result) chatReply = typeof first.result === 'string' ? first.result : JSON.stringify(first.result);
          }
          if (!chatReply) chatReply = 'Agent 处理完成（无文本返回），状态：' + statusLabel(merged.status);
          addAIMessage('ai', chatReply);
        } else {
          addAIMessage('system', '对话处理' + statusLabel(merged.status) + '：' + (merged.message || ''));
        }
      } else {
        var summary = '任务完成：' + statusLabel(merged.status);
        if (merged.completed_items != null) summary += '，成功 ' + merged.completed_items;
        if (merged.failed_items != null && merged.failed_items > 0) summary += '，失败 ' + merged.failed_items;
        addAIMessage('system', summary);
      }
      restartJobPollingAll();
    }
  }

  function statusLabel(status) {
    var map = {
      submitting: '提交中',
      accepted: '已受理',
      queued: '排队中',
      running: '运行中',
      local_running: '本地执行中',
      completed: '已完成',
      failed: '失败',
      partial_failed: '部分失败',
      cancelled: '已取消',
      canceled: '已取消'
    };
    return map[status] || status || '未知';
  }

  function renderJobStatusPanel() {
    var boxes = document.querySelectorAll('[data-job-status-panel]');
    if (!boxes.length) return;
    boxes.forEach(function (box) { box.innerHTML = buildJobStatusHtml(); });
  }

  function buildJobStatusHtml() {
    var job = state.activeJob;
    if (!job) return '<p class="hint">暂无进行中的 job。提交属性审核或 Agent 任务后，会在这里显示排队与进度。</p>';

    var progress = Math.max(0, Math.min(100, Number(job.progress_percent || 0)));
    var terminal = isTerminalJobStatus(job.status);
    var statusClass = job.status === 'failed' ? 'error' : (job.status === 'partial_failed' ? 'warning' : (terminal ? 'done' : 'running'));
    var submittedCount = job.payload && job.payload.components ? job.payload.components.length : 0;
    var resultsHtml = '';
    if (terminal && job.results && Array.isArray(job.results) && job.results.length > 0) {
      var items = job.results.map(function (item) {
        var name = item.component || item.name || item.item_id || '-';
        var itemOk = item.success === true || item.status === 'completed';
        var itemLabel = itemOk ? '✓ 通过' : '✗ 失败';
        var itemClass = itemOk ? 'result-ok' : 'result-fail';
        var errCode = item.error_code || '';
        var msg = item.message || '';
        var detail = errCode ? esc(errCode) : (msg ? esc(msg) : '');
        return '<div class="result-item ' + itemClass + '">' +
          '<span class="result-name">' + esc(name) + '</span>' +
          '<span class="result-badge">' + itemLabel + '</span>' +
          (detail ? '<span class="result-detail">' + detail + '</span>' : '') +
        '</div>';
      }).join('');
      resultsHtml = '<div class="job-results">' +
        '<div class="job-results-head">审核结果</div>' +
        '<div class="result-list">' + items + '</div>' +
      '</div>';
    }
    return '' +
      '<div class="job-panel ' + statusClass + '">' +
        '<div class="job-panel-head">' +
          '<div>' +
            '<div class="job-title">任务' + (job.accepted ? '已受理' : '提交中') + '</div>' +
            '<div class="job-subtitle">' + esc(job.source || 'Agent Job') + '</div>' +
          '</div>' +
          '<span class="badge badge-status ' + (statusClass === 'error' ? 'error' : statusClass === 'warning' ? 'warning' : statusClass === 'running' ? 'warning' : 'done') + '">' + esc(statusLabel(job.status)) + '</span>' +
        '</div>' +
        '<div class="job-grid">' +
          '<div><span>Job ID</span><b>' + esc(job.job_id || '-') + '</b></div>' +
          '<div><span>队列位置</span><b>' + esc(job.queue_position != null ? job.queue_position : '-') + '</b></div>' +
          '<div><span>预计等待</span><b>' + esc(formatSeconds(job.estimated_wait_seconds)) + '</b></div>' +
          '<div><span>当前阶段</span><b>' + esc(job.current_stage || '-') + '</b></div>' +
          (submittedCount > 0 ? '<div><span>已提交组件</span><b>' + submittedCount + ' 个</b></div>' : '') +
          '<div><span>提交时间</span><b>' + esc(formatDateTime(job.submitted_at)) + '</b></div>' +
          '<div><span>开始时间</span><b>' + esc(formatDateTime(job.started_at)) + '</b></div>' +
          '<div><span>完成时间</span><b>' + esc(formatDateTime(job.completed_at)) + '</b></div>' +
          '<div><span>总耗时</span><b>' + esc(getTaskDurationText(job, job.status)) + '</b></div>' +
        '</div>' +
        '<div class="job-progress">' +
          '<div class="progress-wrap">' +
            '<div class="progress-track"><div class="progress-bar" style="width:' + progress + '%"></div></div>' +
            '<span class="progress-text">' + Math.round(progress) + '%</span>' +
          '</div>' +
          '<div class="job-counts">总数 ' + esc(job.total_items != null ? job.total_items : '-') +
            ' / 已完成 ' + esc(job.completed_items != null ? job.completed_items : '-') +
            ' / 失败 ' + esc(job.failed_items != null ? job.failed_items : '-') + '</div>' +
        '</div>' +
        (job.message ? '<p class="' + (job.status === 'failed' ? 'warn' : 'hint') + '">' + esc(job.message) + '</p>' : '') +
        resultsHtml +
      '</div>';
  }

  function pick(obj, pascalKey, camelKey, snakeKey, fallback) {
    obj = obj || {};
    if (obj[pascalKey] != null) return obj[pascalKey];
    if (obj[camelKey] != null) return obj[camelKey];
    if (obj[snakeKey] != null) return obj[snakeKey];
    return fallback;
  }

  function applyRuntimeConfig(runtimeConfig) {
    if (!runtimeConfig) return;
    var agent = runtimeConfig.agent_server || runtimeConfig.AgentServer || {};
    var hindsight = runtimeConfig.hindsight || runtimeConfig.Hindsight || {};

    state.settings.executionMode = pick(runtimeConfig, 'ExecutionMode', 'executionMode', 'execution_mode', state.settings.executionMode);
    state.settings.hermesUrl = pick(agent, 'BaseUrl', 'baseUrl', 'base_url', state.settings.hermesUrl);
    state.settings.contextMode = pick(agent, 'ContextModeDefault', 'contextModeDefault', 'context_mode_default', state.settings.contextMode);
    state.settings.ragProvider = 'hindsight';
    state.settings.ragDbPath = pick(hindsight, 'SourceDbPath', 'sourceDbPath', 'source_db_path', state.settings.ragDbPath);
    state.settings.ragCollection = pick(hindsight, 'Bank', 'bank', 'bank', state.settings.ragCollection);
    state.settings.ragTopK = Number(pick(hindsight, 'TopK', 'topK', 'top_k', state.settings.ragTopK)) || state.settings.ragTopK;
    state.settings.ragScoreThreshold = Number(pick(hindsight, 'ScoreThreshold', 'scoreThreshold', 'score_threshold', state.settings.ragScoreThreshold));
    if (isNaN(state.settings.ragScoreThreshold)) state.settings.ragScoreThreshold = 0.35;
    state.ragOnline = !!pick(hindsight, 'Enabled', 'enabled', 'enabled', state.ragOnline);
    // CKP-004-19 Bug 7: 从 config 读取自定义属性列映射
    applyPropertyColumnMapping(runtimeConfig);
    syncPropertyColumnsFromReadPropertyNames(runtimeConfig);
  }

  // CKP-004-19 Bug 7: 可配置的自定义属性列映射
  var _propColumnMappingLoaded = false;
  function applyPropertyColumnMapping(runtimeConfig) {
    if (_propColumnMappingLoaded) return;
    var mapping = runtimeConfig && runtimeConfig.property_column_mapping;
    if (!Array.isArray(mapping) || mapping.length === 0) return;
    // 重建 PROP_COLUMNS: 固有列 + 用户映射列
    var intrinsic = [
      { key: 'fileName', label: '文件名称', intrinsic: true },
      { key: 'docType', label: '文件类型', intrinsic: true },
      { key: 'instanceCount', label: '实例数', intrinsic: true },
      { key: 'filePath', label: '文件路径', intrinsic: true },
      { key: 'fileSize', label: '文件大小', intrinsic: true }
    ];
    var mapped = [];
    mapping.forEach(function (item) {
      if (item.label && item.property) {
        mapped.push({ key: item.property, label: item.label });
        // 同步更新 PROP_ALIASES 使 resolvePropValue 能直接匹配 property 名
        if (!PROP_ALIASES[item.property]) {
          PROP_ALIASES[item.property] = [item.property];
        }
      }
    });
    if (mapped.length > 0) {
      PROP_COLUMNS = intrinsic.concat(mapped);
      _propColumnMappingLoaded = true;
    }
  }

  function syncPropertyColumnsFromReadPropertyNames(runtimeConfig) {
    if (_propColumnMappingLoaded) return;
    var names = [];
    var cfg = runtimeConfig && (runtimeConfig.read_property_names || runtimeConfig.ReadPropertyNames);
    if (Array.isArray(cfg) && cfg.length > 0) names = cfg;
    else names = DEFAULT_KEY_PROPERTIES;
    var intrinsic = [
      { key: 'fileName', label: '文件名称', intrinsic: true },
      { key: 'docType', label: '文件类型', intrinsic: true },
      { key: 'instanceCount', label: '实例数', intrinsic: true },
      { key: 'filePath', label: '文件路径', intrinsic: true },
      { key: 'fileSize', label: '文件大小', intrinsic: true }
    ];
    var mapped = names.map(function (n) {
      if (!PROP_ALIASES[n]) PROP_ALIASES[n] = [n];
      return { key: n, label: n };
    });
    PROP_COLUMNS = intrinsic.concat(mapped);
  }

  // CKP-004-10: Hermes 状态辅助函数
  function hermesStatusLabel(status) {
    var labels = {
      unknown: '未检测', checking: '检测中...', online: '在线',
      auth_required: '鉴权失败', reachable_wrong_method: '服务可达，端点需校验',
      offline: '离线', error: '异常'
    };
    return labels[status] || status || '未知';
  }
  function hermesStatusDot(status) {
    var dots = {
      unknown: 'offline', checking: 'online', online: 'online',
      auth_required: 'offline', reachable_wrong_method: 'offline', offline: 'offline', error: 'offline'
    };
    return dots[status] || 'offline';
  }
  function isHermesUsable() {
    return state.hermesStatus.status === 'online';
  }

  // ══════════════════════════════════════════════════════════
  //  Context 标准化
  // ══════════════════════════════════════════════════════════
  function normalizeContext(context) {
    if (!context) return null;
    if (typeof context === 'string') {
      try { context = JSON.parse(context); } catch (e) { console.error('[MechPilot] Invalid context JSON:', e); return null; }
    }

    if (context.tree && context.propertyDefs) return context;

    var doc = context.ActiveDocument || context.activeDocument || {};
    var client = context.Client || context.client || {};
    var table = context.PropertyTable || context.propertyTable || {};
    var sourceRows = table.Rows || table.rows || [];
    var columns = table.DynamicColumns || table.dynamicColumns || [];
    var assemblyTree = context.AssemblyTree || context.assemblyTree || [];
    var rowByPivot = {};

    sourceRows.forEach(function (row) {
      var pivot = row.PivotKey || row.pivotKey || row.RowKey || row.rowKey || row.DisplayName || row.displayName;
      if (pivot) rowByPivot[pivot] = row;
    });

    // CKP-004-19: 备用索引 — 多实例同一 filePath 的 PivotKey 可能不匹配（ReadAssemblyAllComponents 按 filePath 分组取首实例名）
    var rowByFilePath = {};
    sourceRows.forEach(function (row) {
      var fp = row.FilePath || row.filePath || '';
      if (fp && !rowByFilePath[fp]) rowByFilePath[fp] = row;
    });

    // CKP-005-08: 按组件名+配置回退（树 PivotKey 含「不可用」时与属性行 PivotKey 不一致）
    var rowByCompKey = {};
    sourceRows.forEach(function (row) {
      var comp = (row.LocalComponentName || row.localComponentName || row.TargetName || row.targetName || '').trim();
      var cfg = (row.Configuration || row.configuration || row.ConfigurationName || row.configurationName || '(默认)').trim();
      if (!comp) return;
      var key = comp.toLowerCase() + '|' + cfg.toLowerCase();
      if (!rowByCompKey[key]) rowByCompKey[key] = row;
    });

    function lookupPropertyRow(node, pivot) {
      var row = rowByPivot[pivot];
      if (row && (row.Properties || row.properties || isUsableFilePath(row.FilePath || row.filePath))) return row;
      var nodeFp = node.FilePath || node.filePath || '';
      if (isUsableFilePath(nodeFp) && rowByFilePath[nodeFp]) return rowByFilePath[nodeFp];
      var comp = (node.ComponentName || node.componentName || node.DisplayName || node.displayName || node.Name || node.name || '').trim();
      var cfg = (node.Configuration || node.configuration || '(默认)').trim();
      if (comp) {
        var ck = comp.toLowerCase() + '|' + cfg.toLowerCase();
        if (rowByCompKey[ck]) return rowByCompKey[ck];
        var ckDefault = comp.toLowerCase() + '|(默认)';
        if (rowByCompKey[ckDefault]) return rowByCompKey[ckDefault];
      }
      return row || {};
    }

    function mapNode(node) {
      var pivot = node.PivotKey || node.pivotKey || node.NodeId || node.nodeId;
      var row = lookupPropertyRow(node, pivot);
      var nodeId = node.NodeId || node.nodeId || pivot || row.RowKey || row.rowKey || row.DisplayName || row.displayName;
      var displayName = getCleanDisplayName(node, row);
      var treeName = cleanNodeName(displayName, false);
      var mappedType = normalizeTreeDocType(node, row);
      var mergedFp = mergeNodeFilePath(node, row);
      return {
        id: nodeId,
        name: treeName,
        displayName: displayName,
        type: mappedType,
        docType: node.DocType || node.docType || row.DocType || row.docType || mappedType,
        quantity: node.Quantity || node.quantity || row.Quantity || row.quantity || 1,
        filePath: mergedFp,
        fileSize: row.FileSize || row.fileSize || node.FileSize || node.fileSize || '',
        pivotKey: node.PivotKey || node.pivotKey || row.PivotKey || row.pivotKey || pivot || '',
        instancePath: node.InstancePath || node.instancePath || row.InstancePath || row.instancePath || '',
        componentName: node.ComponentName || node.componentName || row.LocalComponentName || row.localComponentName || '',
        configuration: node.Configuration || node.configuration || row.Configuration || row.configuration || row.ConfigurationName || row.configurationName || '',
        documentKey: row.DocumentKey || row.documentKey || node.DocumentKey || node.documentKey || '',
        isPart: mappedType === 'part',
        isAssembly: mappedType === 'assembly',
        isSuppressed: node.IsSuppressed || node.isSuppressed || false,
        isLightweight: node.IsLightweight || node.isLightweight || false,
        isHidden: node.IsHidden || node.isHidden || false,
        isEnvelope: node.IsEnvelope || node.isEnvelope || false,
        isVirtual: node.IsVirtual || node.isVirtual || false,
        isReadOnly: node.IsReadOnly || node.isReadOnly || false,
        isInPdmVault: node.IsInPdmVault || node.isInPdmVault || row.IsInPdmVault || row.isInPdmVault || false,
        depth: node.Depth || node.depth || 0,
        childrenCount: node.ChildrenCount || node.childrenCount || 0,
        properties: mapRowProperties(row),
        children: (node.Children || node.children || []).map(mapNode)
      };
    }

    var rootChildren = assemblyTree.map(mapNode);
    if (rootChildren.length === 0) {
      rootChildren = sourceRows.map(function (row, index) {
        var id = row.PivotKey || row.pivotKey || row.RowKey || row.rowKey || ('row-' + index);
        // CKP-004-18: 禁止 PivotKey/filePath 作为 UI name
        var cleanName = getCleanDisplayName({}, row);
        var rowType = normalizeTreeDocType({}, row);
        return {
          id: id,
          name: cleanName,
          displayName: cleanName,
          type: rowType,
          docType: row.DocType || row.docType || rowType,
          quantity: row.Quantity || row.quantity || 1,
          filePath: row.FilePath || row.filePath || '',
          fileSize: row.FileSize || row.fileSize || '',
          pivotKey: row.PivotKey || row.pivotKey || id,
          documentKey: row.DocumentKey || row.documentKey || '',
          isPart: rowType === 'part',
          isAssembly: rowType === 'assembly',
          isSuppressed: row.IsSuppressed || row.isSuppressed || false,
          isLightweight: row.IsLightweight || row.isLightweight || false,
          isHidden: row.IsHidden || row.isHidden || false,
          isEnvelope: row.IsEnvelope || row.isEnvelope || false,
          isVirtual: row.IsVirtual || row.isVirtual || false,
          isReadOnly: row.IsReadOnly || row.isReadOnly || false,
          isInPdmVault: row.IsInPdmVault || row.isInPdmVault || false,
          properties: mapRowProperties(row),
          children: []
        };
      });
    }

    var summary = context.Summary || context.summary || null;
    var warnings = context.Warnings || context.warnings || [];
    var runtimeConfig = context.RuntimeConfig || context.runtimeConfig || {};

    return {
      fileName: doc.Title || doc.title || table.TargetLabel || table.targetLabel || '(none)',
      filePath: doc.FilePath || doc.filePath || '',
      mode: client.ExecutionMode || client.executionMode || context.mode || 'local',
      status: '真实数据',
      timestamp: context.TimestampUtc || context.timestampUtc || context.timestamp || new Date().toISOString(),
      propertyDefs: columns.map(function (name) { return { key: name, label: name, type: 'text' }; }),
      tree: {
        id: 'root',
        name: doc.Title || doc.title || table.TargetLabel || table.targetLabel || '当前文档',
        type: doc.DocType || doc.docType || 'assembly',
        docType: doc.DocType || doc.docType || '',
        quantity: 1,
        filePath: doc.FilePath || doc.filePath || '',
        fileSize: doc.FileSize || doc.fileSize || '',
        properties: {},
        children: rootChildren
      },
      _summary: summary ? {
        targetCount: summary.TargetCount != null ? summary.TargetCount : (summary.targetCount || 0),
        totalComponents: summary.TotalComponents != null ? summary.TotalComponents : (summary.totalComponents || 0),
        uniqueDocCount: summary.UniqueDocCount != null ? summary.UniqueDocCount : (summary.uniqueDocCount || 0),
        partCount: summary.PartCount != null ? summary.PartCount : (summary.partCount || 0),
        subAssemblyCount: summary.SubAssemblyCount != null ? summary.SubAssemblyCount : (summary.subAssemblyCount || 0),
        suppressedCount: summary.SuppressedCount != null ? summary.SuppressedCount : (summary.suppressedCount || 0),
        lightweightCount: summary.LightweightCount != null ? summary.LightweightCount : (summary.lightweightCount || 0),
        readFailedCount: summary.ReadFailedCount != null ? summary.ReadFailedCount : (summary.readFailedCount || 0)
      } : null,
      _warnings: warnings,
      _runtimeConfig: runtimeConfig,
      _propertyIndex: {
        byPivot: rowByPivot,
        byFilePath: rowByFilePath,
        byCompKey: rowByCompKey
      },
      _isMock: false
    };
  }

  // ══════════════════════════════════════════════════════════
  //  初始化
  // ══════════════════════════════════════════════════════════
  function init() {
    loadPersistedState();  // Restore snapshots, task drafts, current task from localStorage
    restartJobPollingAll();
    state.context = normalizeContext(window.MECHPILOT_MOCK_CONTEXT) || null;
    if (state.context) {
      state.settings.executionMode = state.context.mode || 'local';
      applyRuntimeConfig(state.context._runtimeConfig);
      // 默认高亮根节点；默认勾选所有有效零件
      state.selectedNode = state.context.tree;
      initDefaultCheckedNodeIds();
      // 默认展开根节点和第一层
      state.expandedSet.add(state.context.tree.id);
      if (state.context.tree.children) {
        state.context.tree.children.forEach(function (c) { state.expandedSet.add(c.id); });
      }
    }
    injectBridge();
    installWindowControls();
    installSidebar();
    installAIPanel();
    window.addEventListener('beforeunload', clearJobPollTimer);
    navigatePage(state.currentPage);
    renderTopbar();
    renderStatusbar();
  }

  // ══════════════════════════════════════════════════════════
  //  WebView2 Bridge
  // ══════════════════════════════════════════════════════════
  // WebView2 Bridge: C# Base64 为 UTF-8 字节，atob  alone 会破坏中文
  function decodeBase64Utf8Json(b64) {
    if (!b64) return null;
    var binary = atob(b64);
    var len = binary.length;
    var bytes = new Uint8Array(len);
    for (var i = 0; i < len; i++) bytes[i] = binary.charCodeAt(i);
    if (typeof TextDecoder !== 'undefined') {
      return JSON.parse(new TextDecoder('utf-8').decode(bytes));
    }
    var pct = '';
    for (var j = 0; j < len; j++) pct += '%' + ('00' + bytes[j].toString(16)).slice(-2);
    return JSON.parse(decodeURIComponent(pct));
  }

  function parseIncomingContext(context) {
    if (context == null) return null;
    if (typeof context === 'string') {
      try { return JSON.parse(context); } catch (e1) {
        try { return decodeBase64Utf8Json(context); } catch (e2) {
          console.error('[MechPilot] Invalid context payload:', e2);
          return null;
        }
      }
    }
    return context;
  }

  function injectBridge() {
    window.MechPilot = {
      decodeBase64Utf8Json: decodeBase64Utf8Json,
      receiveContext: function (context) {
        context = parseIncomingContext(context);
        if (!context) return;
        // CKP-004-07: 保存旧选中节点标识以便刷新后恢复
        var oldSelectedId = state.selectedNode ? state.selectedNode.id : null;
        var oldSelectedName = state.selectedNode ? state.selectedNode.name : null;
        var oldCheckedIds = new Set(state.checkedNodeIds);

        state.context = normalizeContext(context);
        if (state.context) {
          applyRuntimeConfig(state.context._runtimeConfig);
          // CKP-004-07: 尝试按 ID 或名称恢复选中节点
          var restored = false;
          if (oldSelectedId && state.context.tree) {
            state.selectedNode = findNodeById(state.context.tree, oldSelectedId);
            if (state.selectedNode) restored = true;
          }
          if (!restored && oldSelectedName && state.context.tree) {
            state.selectedNode = findNodeByDisplayName(state.context.tree, oldSelectedName);
            if (state.selectedNode) restored = true;
          }
          if (!restored) {
            state.selectedNode = state.context.tree;  // 回退到根节点
          }
          // CKP-004-07: 清理已不存在的勾选节点
          var newCheckedIds = new Set();
          oldCheckedIds.forEach(function (id) {
            if (state.context.tree && findNodeById(state.context.tree, id)) {
              newCheckedIds.add(id);
            }
          });
          state.checkedNodeIds = newCheckedIds;
          if (state.checkedNodeIds.size === 0) initDefaultCheckedNodeIds();
          state.expandedSet.clear();
          state.expandedSet.add(state.context.tree.id);
          if (state.context.tree.children) {
            state.context.tree.children.forEach(function (c) { state.expandedSet.add(c.id); });
          }
        }
        renderTopbar();
        renderStatusbar();
        if (state.currentPage === 'workspace') navigatePage('workspace');
      },
      receiveResult: function (result) {
        if (typeof result === 'string') {
          try { result = JSON.parse(result); } catch (e) { console.error('[MechPilot] Invalid result JSON:', e); }
        }

        var cmd = result.command || result.action || result.type;
        var data = getResultData(result);
        if (!cmd && data.command) cmd = data.command;
        if (handleLocalReviewResult(result)) return;
        if (cmd === 'ai.material.search') {
          renderMaterialResults(result);
          var count = extractResultItems(result).length;
          addAIMessage('system', '物料检索完成，共 ' + count + ' 条结果');
        } else if (cmd === 'ai.assistant.chat') {
          var chatData = getResultData(result);
          if (isCommandSuccess(result) && !(chatData.job_id || chatData.jobId || chatData.id)) {
            handleChatDirectResult(result);
          } else {
            handleJobSubmitResult(result);
          }
        } else if (cmd === 'material.properties.review.submit' || cmd === 'agent.job.submit' || (!cmd && state.activeJob && state.activeJob.status === 'submitting' && (data.job_id || data.jobId || data.id))) {
          handleJobSubmitResult(result);
        } else if (cmd === 'agent.job.poll') {
          handleJobPollResult(result);
        } else if (!cmd && data && (data.job_id || data.jobId) && getJobById(data.job_id || data.jobId)) {
          handleJobPollResult(result);
        } else if (cmd === 'local.read_properties') {
          if (applyContextFromResult(data, null)) {
            var readName = state.context && state.context.fileName ? state.context.fileName : '';
            showToast('属性读取完成：' + readName + '，已刷新属性表');
          } else if (isCommandSuccess(result)) {
            showToast('属性读取完成');
          } else {
            showToast('属性读取失败：' + (result.message || (result.error && result.error.message) || ''));
          }
        } else if (cmd === 'refresh_context' || (!cmd && data && (data.context_json || data.contextJson || data.context) && isCommandSuccess(result))) {
          if (applyContextFromResult(data, null)) {
            var fileName = (state.context && state.context.fileName) || '';
            showToast('上下文已刷新：' + fileName + '（历史选择已保存在快照中）');
          } else if (isCommandSuccess(result)) {
            showToast('上下文刷新完成');
          } else {
            showToast('上下文刷新失败：' + (result.message || ''));
          }
        } else if (!cmd && data && data.pinned !== undefined) {
          // CKP-004-08: window_pin_toggle result
          state.windowPinned = !!data.pinned;
          renderTopbar();
          showToast(data.pinned ? '已钉住 — Cockpit 窗口保持前台' : '已取消钉住');
        } else if (cmd === 'agent.health.check' || (!cmd && data && data.status && (data.status === 'online' || data.status === 'offline' || data.status === 'auth_required' || data.status === 'reachable_wrong_method' || data.status === 'error' || data.status === 'checking'))) {
          // CKP-004-10: Hermes health check result
          if (data && data.status) {
            state.hermesStatus.status = data.status || 'error';
            state.hermesStatus.message = data.message || '';
            state.hermesStatus.base_url = data.base_url || '';
            state.hermesStatus.endpoint = data.endpoint || '';
            state.hermesStatus.http_status = data.http_status || null;
            state.hermesStatus.checked_at = data.checked_at || new Date().toISOString();
            state.hermesStatus.duration_ms = data.duration_ms || null;
            // Sync legacy boolean
            state.hermesOnline = data.status === 'online';
            // Refresh dashboard if visible
            if (state.currentPage === 'dashboard') navigatePage('dashboard');
            showToast('Hermes: ' + hermesStatusLabel(data.status));
          }
        } else {
          // Hermes AI response has data.content; show as 'ai' message
          if (data && data.content) {
            addAIMessage('ai', data.content);
          } else if (result.ok && result.message) {
            addAIMessage('system', result.message);
          } else {
            addAIMessage('system', result.message || JSON.stringify(result));
          }
        }
      },
      navigate_page: function (pageId) {
        if (PAGES[pageId]) navigatePage(pageId);
      },
      set_selected_node: function (nodeId) {
        var node = findNodeById(state.context ? state.context.tree : null, nodeId);
        if (node) {
          state.selectedNode = node;
          if (state.currentPage === 'workspace') updateOverviewSummary();
        }
      },
      sendCommand: function (type, payload) {
        var requestId = 'req-' + Date.now() + '-' + Math.random().toString(36).slice(2, 12);
        var envelope = JSON.stringify({
          command: type,
          type: type,
          request_id: requestId,
          payload: payload || {},
          ts: Date.now()
        });
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage(envelope);
        } else {
          // Mock 环境下（非 WebView2）仅在开发工具打开时日志输出
          // Mock 环境下直接返回物料检索结果
          if (type === 'ai.assistant.chat') {
            setTimeout(function () {
              window.MechPilot.receiveResult({
                request_id: requestId,
                command: 'ai.assistant.chat',
                ok: true,
                data: {
                  job_id: 'job-mock-chat-' + Date.now(),
                  accepted: true,
                  status: 'queued',
                  queue_position: 1,
                  estimated_wait_seconds: 5,
                  progress_percent: 0,
                  current_stage: '排队中'
                }
              });
            }, 400);
          } else if (type === 'ai.material.search') {
            setTimeout(function () {
              window.MechPilot.receiveResult({
                request_id: requestId,
                command: 'ai.material.search',
                ok: true,
                data: {
                  source: 'hindsight',
                  items: [
                    { name: '直线导轨 MGN12', spec: 'MGN12-400mm', material: 'SUS304', supplier: 'HIWIN', drawing_no: 'MGN-12-400', score: 0.92, snippet: '不锈钢材质，适用于高精度场合...' },
                    { name: '直线导轨 MGN15', spec: 'MGN15-600mm', material: 'SUS304', supplier: 'HIWIN', drawing_no: 'MGN-15-600', score: 0.88, snippet: '承载能力强，适合重载应用...' },
                    { name: '不锈钢滑块', spec: 'MGN12H', material: 'SUS304', supplier: 'HIWIN', drawing_no: 'MGN-12H', score: 0.85, snippet: '精密级滑块，预紧可调...' },
                    { name: '直线轴承', spec: 'LME8UU', material: '轴承钢', supplier: 'THK', drawing_no: 'LME-8UU', score: 0.78, snippet: '标准直线轴承，润滑良好...' }
                  ]
                }
              });
            }, 500);
          } else if (type === 'material.properties.review.submit' || type === 'agent.job.submit') {
            setTimeout(function () {
              window.MechPilot.receiveResult({
                request_id: requestId,
                command: type,
                success: true,
                data: {
                  job_id: 'job-mock-' + Date.now(),
                  accepted: true,
                  status: 'queued',
                  queue_position: 2,
                  estimated_wait_seconds: 18,
                  total_items: payload && payload.components ? payload.components.length : 1,
                  completed_items: 0,
                  failed_items: 0,
                  progress_percent: 0,
                  current_stage: '排队中'
                }
              });
            }, 400);
          } else if (type === 'agent.job.poll') {
            setTimeout(function () {
              var current = state.activeJob || {};
              var progress = Math.min(100, Number(current.progress_percent || 0) + 35);
              window.MechPilot.receiveResult({
                request_id: requestId,
                command: 'agent.job.poll',
                success: true,
                data: {
                  job_id: payload && payload.job_id,
                  accepted: true,
                  status: progress >= 100 ? 'completed' : 'running',
                  queue_position: progress > 0 ? 0 : current.queue_position,
                  estimated_wait_seconds: Math.max(0, Number(current.estimated_wait_seconds || 0) - 3),
                  total_items: current.total_items || 1,
                  completed_items: progress >= 100 ? (current.total_items || 1) : current.completed_items || 0,
                  failed_items: 0,
                  progress_percent: progress,
                  current_stage: progress >= 100 ? '完成' : '处理中'
                }
              });
            }, 300);
          }
        }
        return requestId;
      }
    };
    // CKP-004-23: Object.freeze 防止 bridge 被篡改
    if (typeof Object.freeze === 'function') {
      try { Object.freeze(window.MechPilot); } catch (_) { /* readonly 模式忽略 */ }
    }
  }

  // ══════════════════════════════════════════════════════════
  //  顶栏 + Window Controls
  // ══════════════════════════════════════════════════════════
  function renderTopbar() {
    var c = state.context;
    var el = document.getElementById('topbar');
    el.innerHTML =
      '<div class="topbar-brand"><span class="logo-box">MP</span> MechPilot Agent驾驶舱</div>' +
      '<div class="topbar-divider"></div>' +
      '<div class="topbar-info">' +
        '<span><span class="label">路径：</span><span class="value" id="tb-path" title="' + esc(c ? c.filePath : '') + '">' + esc(c ? c.filePath : '') + '</span></span>' +
      '</div>' +
      '<div class="topbar-right">' +
        // CKP-004-08: 钉住/置顶按钮
        '<button class="topbar-pin-btn' + (state.windowPinned ? ' pinned' : '') + '" id="topbar-pin-btn" title="' + (state.windowPinned ? '已钉住 — 点击取消' : '钉住 — 窗口保持前台') + '">' +
          (state.windowPinned ? '📌 已钉住' : '📌 钉住') +
        '</button>' +
      '</div>';
    ensureWindowControls();
  }

  function installWindowControls() {
    var observer = new MutationObserver(function () { ensureWindowControls(); });
    observer.observe(document.getElementById('app'), { childList: true, subtree: true });
    ensureWindowControls();
    document.addEventListener('mousedown', function (e) {
      if (e.button !== 0) return;
      var topbar = e.target.closest('.topbar');
      if (!topbar) return;
      if (e.target.closest('.window-controls,button,input,select,textarea,a,[role="button"],.badge')) return;
      e.preventDefault();
      window.MechPilot.sendCommand('window_drag', {});
    });
    document.addEventListener('dblclick', function (e) {
      var topbar = e.target.closest('.topbar');
      if (!topbar) return;
      if (e.target.closest('.window-controls,button,input,select,textarea,a,[role="button"],.badge')) return;
      e.preventDefault();
      window.MechPilot.sendCommand('window_maximize', {});
    });
  }

  function ensureWindowControls() {
    var topbar = document.getElementById('topbar');
    if (!topbar) return;

    // CKP-004-08: Bind pin toggle button
    var pinBtn = topbar.querySelector('#topbar-pin-btn');
    if (pinBtn && !pinBtn._boundPin) {
      pinBtn._boundPin = true;
      pinBtn.addEventListener('click', function (e) {
        e.preventDefault(); e.stopPropagation();
        state.windowPinned = !state.windowPinned;
        window.MechPilot.sendCommand('window_pin_toggle', { pinned: state.windowPinned });
      });
    }

    // Window controls
    var controls = topbar.querySelector('.window-controls');
    if (controls) return;
    controls = document.createElement('div');
    controls.className = 'window-controls';
    controls.setAttribute('aria-label', 'Window controls');
    controls.innerHTML =
      '<button class="window-btn" data-window-command="window_minimize" title="最小化" aria-label="Minimize"><span></span></button>' +
      '<button class="window-btn" data-window-command="window_maximize" title="最大化 / 还原" aria-label="Maximize or restore"><span></span></button>' +
      '<button class="window-btn window-close" data-window-command="window_close" title="关闭" aria-label="Close"><span></span></button>';
    topbar.appendChild(controls);
    controls.querySelectorAll('.window-btn').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.preventDefault(); e.stopPropagation();
        window.MechPilot.sendCommand(this.getAttribute('data-window-command'), {});
      });
    });
  }

  // ══════════════════════════════════════════════════════════
  //  状态栏
  // ══════════════════════════════════════════════════════════
  function renderStatusbar() {
    var c = state.context;
    var el = document.getElementById('statusbar');
    var isMock = !c || c._isMock;
    var compCount = c && c.tree ? countNodes(c.tree) - 1 : 0;
    var partCount = c && c.tree ? countParts(c.tree) : 0;
    var selName = state.selectedNode ? state.selectedNode.name : '(无)';
    var checkedParts = getCheckedPartCount();
    var jobText = state.activeJob ? ('Job：' + statusLabel(state.activeJob.status)) : '就绪';
    var jobTone = state.activeJob && state.activeJob.status === 'failed' ? 'error' : (state.activeJob && !isTerminalJobStatus(state.activeJob.status) ? 'warning' : '');
    el.innerHTML =
      '<div class="statusbar-row">' +
        '<span class="status-item"><span class="dot ' + (isMock ? 'dot-mock' : 'dot-real') + '"></span>' + (isMock ? '演示数据' : '真实数据') + '</span>' +
        '<span class="sep"></span>' +
        '<span class="status-item">组件：<b>' + compCount + '</b></span>' +
        '<span class="status-item">零件：<b>' + partCount + '</b></span>' +
        '<span class="sep"></span>' +
        '<span class="status-item">选中：<b>' + esc(selName) + '</b></span>' +
        '<span class="status-item">已勾选：<b>' + getCheckedNodeCount() + '</b></span>' +
        '<span class="status-item">勾选零件：<b>' + checkedParts + '</b></span>' +
        '<span class="sep"></span>' +
        '<span class="status-item ' + jobTone + '" id="status-agent">' + esc(jobText) + '</span>' +
      '</div>';
  }

  // ══════════════════════════════════════════════════════════
  //  Sidebar 导航
  // ══════════════════════════════════════════════════════════
  function installSidebar() {
    var nav = document.getElementById('sidebar-nav');
    nav.querySelectorAll('li').forEach(function (li) {
      li.addEventListener('click', function () {
        var page = this.getAttribute('data-page');
        if (page) navigatePage(page);
      });
    });
    // CKP-004-19: 侧边栏折叠/展开（默认折叠）
    var toggle = document.getElementById('sidebar-toggle');
    var sidebar = document.getElementById('sidebar');
    if (toggle) {
      // 启动时应用默认折叠状态
      if (state.sidebarCollapsed) {
        sidebar.classList.add('collapsed');
        toggle.innerHTML = '▶';
        toggle.setAttribute('title', '展开导航');
      }
      toggle.addEventListener('click', function () {
        state.sidebarCollapsed = !state.sidebarCollapsed;
        sidebar.classList.toggle('collapsed', state.sidebarCollapsed);
        toggle.innerHTML = state.sidebarCollapsed ? '▶' : '◀';
        toggle.setAttribute('title', state.sidebarCollapsed ? '展开导航' : '收起导航');
      });
    }
  }

  function navigatePage(pageId) {
    if (!PAGES[pageId]) pageId = DEFAULT_PAGE;
    state.currentPage = pageId;

    var nav = document.getElementById('sidebar-nav');
    nav.querySelectorAll('li').forEach(function (li) {
      li.classList.toggle('active', li.getAttribute('data-page') === pageId);
    });

    var container = document.getElementById('page-container');
    container.classList.toggle('page-dashboard-active', pageId === 'dashboard');
    container.classList.toggle('page-workspace-active', pageId === 'workspace');
    container.innerHTML = '';
    // CKP-004-23 P1-3.3: 错误边界保护渲染不中断整个 App
    try {
      PAGES[pageId].render(container);
    } catch (e) {
      console.error('[MechPilot] Page render error:', pageId, e);
      container.innerHTML = '<div class="error-message">页面渲染失败: ' + esc(String(e.message || e)) + '</div>';
    }
  }

  // ══════════════════════════════════════════════════════════
  //  右侧 AI 面板
  // ══════════════════════════════════════════════════════════
  function installAIPanel() {
    var panel = document.getElementById('ai-panel');
    var toggle = document.getElementById('ai-panel-toggle');
    var sendBtn = document.getElementById('ai-send');
    var input = document.getElementById('ai-input');

    toggle.addEventListener('click', function () {
      state.aiPanelOpen = !state.aiPanelOpen;
      panel.classList.toggle('collapsed', !state.aiPanelOpen);
      toggle.innerHTML = state.aiPanelOpen ? '&#x25B6;' : '&#x25C0;';
      toggle.setAttribute('title', state.aiPanelOpen ? '收起' : '展开');
    });

    sendBtn.addEventListener('click', function () { doSendAI(); });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); doSendAI(); }
    });

    // CKP-004-22: 初始同步折叠状态
    if (!state.aiPanelOpen) {
      panel.classList.add('collapsed');
      toggle.innerHTML = '&#x25C0;';
      toggle.setAttribute('title', '展开');
    }

    if (state.aiMessages.length === 0) {
      addAIMessage('ai', '你好，我是 MechPilot AI 助手。当前页面：' + PAGES[DEFAULT_PAGE].title + '。有什么可以帮你的？');
    }
  }

  function addAIMessage(role, text) {
    state.aiMessages.push({ role: role, text: text, time: new Date() });
    var box = document.getElementById('ai-messages');
    if (!box) return;
    var div = document.createElement('div');
    div.className = 'ai-message ai-' + role;
    div.innerHTML = '<div class="ai-bubble">' + esc(text) + '</div><div class="ai-time">' + formatTime(new Date()) + '</div>';
    box.appendChild(div);
    box.scrollTop = box.scrollHeight;
  }

  function doSendAI() {
    var input = document.getElementById('ai-input');
    var text = input.value.trim();
    if (!text) return;
    input.value = '';
    addAIMessage('user', text);

    var c = state.context;
    var sel = state.selectedNode;
    var currentTask = getCurrentTask();

    var payload = {
      page: state.currentPage,
      message: text,
      context: c ? {
        fileName: c.fileName,
        filePath: c.filePath,
        mode: c.mode,
        docType: c.tree ? c.tree.docType : ''
      } : null,
      selectedNode: sel ? {
        id: sel.id,
        name: sel.name,
        type: sel.type,
        docType: sel.docType,
        filePath: sel.filePath,
        quantity: sel.quantity
      } : null,
      contextMode: state.settings.contextMode
    };

    // Add task context to payload (CKP-004-05 workflow D)
    if (currentTask) {
      payload.taskContext = {
        taskId: currentTask.taskId,
        taskType: currentTask.taskType,
        title: currentTask.title,
        selectedObjectIds: currentTask.selectedObjectIds,
        selectedObjectNames: currentTask.selectedObjectNames,
        selectedObjectCount: currentTask.selectedObjectCount,
        aiThreadId: currentTask.aiThreadId
      };
    }

    // Set up active job state for chat (same pattern as property review)
    clearJobPollTimer();
    state.activeJob = {
      job_id: '',
      accepted: false,
      status: 'submitting',
      progress_percent: 0,
      current_stage: '提交中',
      source: 'ai.assistant.chat',
      chat_message: text,
      submitted_at: new Date().toISOString(),
      started_at: null,
      completed_at: null,
      request_id: null,
      payload: payload
    };
    renderJobStatusPanel();
    renderStatusbar();

    try {
      state.activeJob.request_id = window.MechPilot.sendCommand('ai.assistant.chat', payload);
      addAIMessage('system', '对话已发送，等待 Agent 响应…');
    } catch (e) {
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '提交失败';
      state.activeJob.message = '发送失败: ' + e.message;
      renderJobStatusPanel();
      renderStatusbar();
      addAIMessage('system', state.activeJob.message);
    }
  }

  // ══════════════════════════════════════════════════════════
  //  设计树渲染（支持树状 / 扁平两种模式）
  // ══════════════════════════════════════════════════════════
  function renderDesignTree(container) {
    var tree = state.context ? state.context.tree : null;
    if (!tree) {
      container.innerHTML = '<div class="tree-empty">等待数据注入…</div>';
      return;
    }

    // 筛选栏
    renderTreeFilterBar();

    var el = document.createElement('div');
    el.className = 'design-tree';
    el.id = 'design-tree';

    if (state.settings.treeViewMode === 'flat') {
      el.appendChild(buildFlatView(tree));
    } else {
      el.appendChild(buildTreeNode(tree, 0));
    }
    container.appendChild(el);
  }

  // ── 扁平汇总视图 (CKP-004-15: clean names, summary header top, categories below) ──
  function buildFlatView(tree) {
    var frag = document.createDocumentFragment();
    var allNodes = [];
    flattenTree(tree, allNodes, 0);

    // Group by type
    var parts = allNodes.filter(function (n) { return isPartNode(n); });
    var assemblies = allNodes.filter(function (n) { return isAssemblyNode(n); });

    // ── CKP-004-15: Header row (名称 + 实例数) at top ──
    var header = document.createElement('div');
    header.className = 'flat-header-row';
    header.innerHTML = '<span class="flat-col-check">☐</span><span class="flat-col-name">名称</span><span class="flat-col-qty">实例数</span>';
    frag.appendChild(header);

    // ── Group by filePath (same document = same group row) ──
    function buildDocGroup(groupLabel, nodeList) {
      var docMap = {};
      nodeList.forEach(function (n) {
        var key = getNodeGroupKey(n);
        if (!docMap[key]) {
          docMap[key] = { node: n, count: 0, groupKey: key, ids: [] };
        }
        docMap[key].count++;
        docMap[key].ids.push(n.id);
      });

      if (Object.keys(docMap).length === 0) return null;

      var section = document.createElement('div');
      section.className = 'flat-section';

      var title = document.createElement('div');
      title.className = 'flat-section-title';

      // 分区全选 checkbox（替代原来的两个按钮）
      var groupCheckAllNodes = nodeList;
      var allGroupKeys = new Set();
      groupCheckAllNodes.forEach(function (n) { allGroupKeys.add(getNodeGroupKey(n)); });
      var allGroupsChecked = true;
      allGroupKeys.forEach(function (gk) { if (!isGroupFullyChecked(gk)) allGroupsChecked = false; });

      var selCb = document.createElement('input');
      selCb.type = 'checkbox';
      selCb.className = 'flat-checkbox';
      selCb.checked = allGroupsChecked;
      selCb.addEventListener('click', function (e) { e.stopPropagation(); });
      selCb.addEventListener('change', function (e) {
        e.stopPropagation();
        var checked = selCb.checked;
        var groups = new Set();
        groupCheckAllNodes.forEach(function (n) { groups.add(getNodeGroupKey(n)); });
        groups.forEach(function (gk) { applyNodeGroupChecked(gk, checked); });
        refreshCheckedUi();
      });

      title.innerHTML =
        '<span class="flat-col-check"></span>' +
        groupLabel + ' (' + Object.keys(docMap).length + ')';
      title.querySelector('.flat-col-check').appendChild(selCb);
      section.appendChild(title);

      var table = document.createElement('div');
      table.className = 'flat-table';

      // Sort alphabetically
      var entries = Object.values(docMap).sort(function (a, b) {
        var na = cleanNodeName(a.node.name, true);
        var nb = cleanNodeName(b.node.name, true);
        return na.localeCompare(nb);
      });

      entries.forEach(function (entry) {
        var n = entry.node;
        var groupKey = entry.groupKey;
        var allChecked = isGroupFullyChecked(groupKey);
        var indeterminate = isGroupPartiallyChecked(groupKey);

        var row = document.createElement('div');
        row.className = 'flat-row';
        row.setAttribute('data-group-key', groupKey);
        if (state.selectedNode && entry.ids.indexOf(state.selectedNode.id) >= 0) row.classList.add('selected');

        // CKP-004-15: clean display name — no format suffix, no instance code
        var displayName = cleanNodeName(n.name, true);
        var badges = '';
        if (n.isSuppressed) badges += ' <span class="tree-badge-suppressed">抑制</span>';
        if (n.isLightweight) badges += ' <span class="tree-badge-lightweight">轻化</span>';

        // Checkbox
        var cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.className = 'flat-checkbox';
        cb.checked = allChecked;
        cb.indeterminate = indeterminate;
        cb.addEventListener('click', function (e) { e.stopPropagation(); });
        cb.addEventListener('change', function (e) {
          e.stopPropagation();
          toggleNodeGroupChecked(groupKey, cb.checked);
        });

        row.innerHTML =
          '<span class="flat-col-check"></span>' +
          '<span class="flat-col-name" title="' + esc(displayName) + '">' +
            '<span class="' + getNodeIconClass(n) + '">' + getNodeIconHtml(n) + '</span>' +
            '<span class="flat-col-name-text">' + esc(displayName) + badges + '</span>' +
          '</span>' +
          '<span class="flat-col-qty">' + entry.count + '</span>';
        row.querySelector('.flat-col-check').appendChild(cb);

        row.addEventListener('click', function (e) {
          if (e.target.closest('.flat-checkbox')) return;
          state.selectedNode = n;
          var newChecked = !isGroupFullyChecked(groupKey);
          toggleNodeGroupChecked(groupKey, newChecked);
        });

        table.appendChild(row);
      });

      section.appendChild(table);
      return section;
    }

    // ── CKP-004-15: Summary bar with counts ──
    var summary = document.createElement('div');
    summary.className = 'flat-summary-header';
    summary.innerHTML =
      '<span class="flat-stat">📦 <b>' + assemblies.length + '</b> 个装配体</span>' +
      '<span class="flat-stat">🔩 <b>' + parts.length + '</b> 个零部件</span>';
    frag.appendChild(summary);

    // ── 装配体组 ──
    var asmSection = buildDocGroup('装配体', assemblies);
    if (asmSection) frag.appendChild(asmSection);

    // ── 零部件组 ──
    var partSection = buildDocGroup('零部件', parts);
    if (partSection) frag.appendChild(partSection);

    return frag;
  }

  function flattenTree(node, result, depth) {
    if (!node) return;
    if (node.id !== 'root' && !isNodeVisible(node)) return;
    node.depth = depth;
    if (node.id !== 'root') result.push(node);  // skip synthetic root
    if (node.children) {
      node.children.forEach(function (child) {
        flattenTree(child, result, depth + 1);
      });
    }
  }

  function buildTreeNode(node, depth) {
    var div = document.createElement('div');
    div.className = 'tree-node';
    div.setAttribute('data-id', node.id);

    // 非根节点：应用筛选器可见性检查
    if (node.id !== 'root' && !isNodeVisible(node)) {
      div.style.display = 'none';
      return div;
    }

    var hasChildren = isAssemblyNode(node) && node.children && node.children.length > 0;
    var isExpanded = state.expandedSet.has(node.id);
    var isSelected = state.selectedNode && state.selectedNode.id === node.id;

    var row = document.createElement('div');
    row.className = 'tree-row' + (isSelected ? ' selected' : '');
    row.style.paddingLeft = (8 + depth * 16) + 'px';

    // 多选 checkbox
    var checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.className = 'tree-checkbox';
    checkbox.setAttribute('aria-label', '勾选 ' + node.name);
    if (isAssemblyNode(node) && hasChildren) {
      var asmState = getAssemblyCheckState(node);
      checkbox.checked = asmState.checked;
      checkbox.indeterminate = asmState.indeterminate;
    } else if (isSubmittableNode(node)) {
      var partGroupKey = getNodeGroupKey(node);
      checkbox.checked = isGroupFullyChecked(partGroupKey);
      checkbox.indeterminate = isGroupPartiallyChecked(partGroupKey);
    } else {
      checkbox.checked = state.checkedNodeIds.has(node.id);
    }
    checkbox.addEventListener('click', function (e) { e.stopPropagation(); });
    checkbox.addEventListener('change', function (e) {
      e.stopPropagation();
      handleNodeCheckToggle(node, checkbox.checked);
    });
    row.appendChild(checkbox);

    // 展开箭头
    var toggle = document.createElement('span');
    toggle.className = 'tree-toggle ' + (hasChildren ? (isExpanded ? 'expanded' : '') : 'leaf');
    toggle.innerHTML = ICONS.chevron;
    if (hasChildren) {
      toggle.addEventListener('click', function (e) {
        e.stopPropagation();
        if (state.expandedSet.has(node.id)) {
          state.expandedSet.delete(node.id);
        } else {
          state.expandedSet.add(node.id);
        }
        refreshDesignTree();
      });
    }
    row.appendChild(toggle);

    // 图标（本地默认色，PDM 文件用 tree-icon-pdm）
    var icon = document.createElement('span');
    icon.className = getNodeIconClass(node);
    icon.innerHTML = getNodeIconHtml(node);
    row.appendChild(icon);

    // 名称
    var name = document.createElement('span');
    name.className = 'tree-name';
    // CKP-004-15: 去掉文件扩展名 (.SLDPRT 等)
    name.textContent = cleanNodeName(node.displayName || node.name || '', false);
    row.appendChild(name);

    // 数量
    if (node.quantity > 1) {
      var qty = document.createElement('span');
      qty.className = 'tree-qty';
      qty.textContent = '×' + node.quantity;
      row.appendChild(qty);
    }

    // 抑制/轻化标记
    if (node.isSuppressed) {
      var badgeSup = document.createElement('span');
      badgeSup.className = 'tree-badge tree-badge-suppressed';
      badgeSup.textContent = '抑制';
      row.appendChild(badgeSup);
    }
    if (node.isLightweight) {
      var badgeLw = document.createElement('span');
      badgeLw.className = 'tree-badge tree-badge-lightweight';
      badgeLw.textContent = '轻化';
      row.appendChild(badgeLw);
    }

    // CKP-004-08: 行主体点击 → 选中节点 + 切换勾选状态
    row.addEventListener('click', function (e) {
      state.selectedNode = node;
      // Toggle checkbox state (checkbox and expand arrow have stopPropagation, so we only get here for name/icon/qty clicks)
      if (!e.target.closest('.tree-toggle') && !e.target.closest('.tree-checkbox')) {
        var newChecked;
        if (isAssemblyNode(node) && hasChildren) {
          newChecked = !getAssemblyCheckState(node).checked;
        } else if (isSubmittableNode(node)) {
          newChecked = !isGroupFullyChecked(getNodeGroupKey(node));
        } else {
          newChecked = !state.checkedNodeIds.has(node.id);
        }
        handleNodeCheckToggle(node, newChecked);
      }
      refreshDesignTree();
      updateOverviewSummary();
      renderStatusbar();
      window.MechPilot.sendCommand('node_selected', { nodeId: node.id, name: node.name });
    });

    div.appendChild(row);

    // 子节点
    if (hasChildren && isExpanded) {
      var children = document.createElement('div');
      children.className = 'tree-children';
      filterVisibleChildren(node.children).forEach(function (child) {
        children.appendChild(buildTreeNode(child, depth + 1));
      });
      div.appendChild(children);
    }

    return div;
  }

  function refreshDesignTree() {
    var container = document.getElementById('design-tree-container');
    if (!container) return;
    container.innerHTML = '';
    renderDesignTree(container);
  }

  // ══════════════════════════════════════════════════════════
  //  总览页面
  // ══════════════════════════════════════════════════════════
  // ══════════════════════════════════════════════════════════
  //  Dashboard（总览 — Add-in 使用态势）
  // ══════════════════════════════════════════════════════════
  function renderDashboard(container) {
    var ctx = state.context || {};
    var fileName = ctx.fileName || '无激活文档';
    var treeNodes = ctx.tree ? countNodes(ctx.tree) - 1 : 0;

    // Mock task statistics (placeholder for real data)
    var mockStats = state.submittedJobs.length > 0 ? computeTaskStats(state.submittedJobs) : {
      total: 12, completed: 9, failed: 1, running: 2,
      successRate: '75%', avgDuration: '4.2s'
    };

    container.innerHTML =
      '<div class="dashboard-page">' +
        '<div class="page-title">总览 <span class="page-subtitle">Add-in 插件使用态势</span></div>' +

        '<div class="dashboard-grid">' +
          // Plugin status card
          '<div class="dash-card dash-status">' +
            '<div class="dash-card-title">插件运行状态</div>' +
            '<div class="dash-card-body">' +
              '<div class="status-row"><span class="status-dot online"></span> Add-in <span class="status-val">已连接</span></div>' +
              '<div class="status-row"><span class="status-dot ' + (ctx.fileName ? 'online' : 'offline') + '"></span> SolidWorks <span class="status-val">' + (ctx.fileName ? fileName : '无文档') + '</span></div>' +
              '<div class="status-row">' +
                '<span class="status-dot ' + hermesStatusDot(state.hermesStatus.status) + '"></span> Hermes ' +
                '<span class="status-val">' + hermesStatusLabel(state.hermesStatus.status) + '</span>' +
                '<button class="dash-refresh-btn" id="btn-hermes-reconnect" title="重新检测 Hermes 连接">' +
                  (state.hermesStatus.status === 'checking' ? '⏳ 检测中...' : '🔄 重新连接') +
                '</button>' +
              '</div>' +
              '<div class="status-row"><span class="status-dot ' + (state.ragOnline ? 'online' : 'offline') + '"></span> RAG <span class="status-val">' + (state.ragOnline ? '在线' : '离线') + '</span></div>' +
            '</div>' +
          '</div>' +

          // Task statistics card
          '<div class="dash-card dash-stats">' +
            '<div class="dash-card-title">任务统计</div>' +
            '<div class="dash-card-body">' +
              '<div class="stats-grid">' +
                '<div class="stat-box"><div class="stat-num">' + mockStats.total + '</div><div class="stat-lbl">总任务</div></div>' +
                '<div class="stat-box"><div class="stat-num ok">' + mockStats.completed + '</div><div class="stat-lbl">成功</div></div>' +
                '<div class="stat-box"><div class="stat-num err">' + mockStats.failed + '</div><div class="stat-lbl">失败</div></div>' +
                '<div class="stat-box"><div class="stat-num warn">' + mockStats.running + '</div><div class="stat-lbl">执行中</div></div>' +
              '</div>' +
              '<div class="stats-meta">成功率 ' + mockStats.successRate + ' · 平均耗时 ' + mockStats.avgDuration + '</div>' +
            '</div>' +
          '</div>' +

          // Task type distribution card
          '<div class="dash-card dash-types">' +
            '<div class="dash-card-title">任务类型分布</div>' +
            '<div class="dash-card-body">' +
              '<div class="type-bar"><span class="type-label">属性审核</span><div class="type-track"><div class="type-fill" style="width:60%"></div></div><span class="type-count">7</span></div>' +
              '<div class="type-bar"><span class="type-label">AI 对话</span><div class="type-track"><div class="type-fill alt" style="width:30%"></div></div><span class="type-count">3</span></div>' +
              '<div class="type-bar"><span class="type-label">物料检索</span><div class="type-track"><div class="type-fill alt2" style="width:10%"></div></div><span class="type-count">1</span></div>' +
              '<div class="type-bar"><span class="type-label">设计计算</span><div class="type-track"><div class="type-fill alt3" style="width:8%"></div></div><span class="type-count">1</span></div>' +
            '</div>' +
          '</div>' +

          // Recent tasks card
          '<div class="dash-card dash-recent">' +
            '<div class="dash-card-title">最近任务</div>' +
            '<div class="dash-card-body">' + renderRecentTasksHtml() + '</div>' +
          '</div>' +

          // Error statistics card
          '<div class="dash-card dash-errors">' +
            '<div class="dash-card-title">异常统计</div>' +
            '<div class="dash-card-body">' +
              '<div class="error-list">' +
                '<div class="error-row"><span class="error-badge">401</span> Hermes 认证失败 <span class="error-count">0</span></div>' +
                '<div class="error-row"><span class="error-badge">404</span> 端点不可达 <span class="error-count">0</span></div>' +
                '<div class="error-row"><span class="error-badge">timeout</span> 请求超时 <span class="error-count">1</span></div>' +
                '<div class="error-row"><span class="error-badge">offline</span> MCP 离线 <span class="error-count">0</span></div>' +
              '</div>' +
            '</div>' +
          '</div>' +

          // Context snapshot card
          '<div class="dash-card dash-snapshots">' +
            '<div class="dash-card-title">上下文快照</div>' +
            '<div class="dash-card-body">' +
              '<div class="snapshot-stats">' +
                '<span>今日快照 <b>' + state.snapshots.length + '</b></span>' +
                '<span>可召回 <b>' + state.snapshots.length + '</b></span>' +
              '</div>' +
              renderSnapshotList() +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>';

    // Bind snapshot restore buttons
    container.querySelectorAll('.snapshot-restore-btn').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.stopPropagation();
        restoreSnapshot(this.getAttribute('data-snapshot-id'));
      });
    });

    // CKP-004-10: Bind Hermes reconnect button
    var hermesBtn = document.getElementById('btn-hermes-reconnect');
    if (hermesBtn) {
      hermesBtn.addEventListener('click', function () {
        state.hermesStatus.status = 'checking';
        state.hermesStatus.message = '';
        // Re-render dashboard card to show '检测中...'
        var card = document.querySelector('.dash-status .dash-card-body');
        if (card) {
          card.querySelectorAll('.status-row')[2].innerHTML =
            '<span class="status-dot online"></span> Hermes <span class="status-val">检测中...</span>' +
            '<button class="dash-refresh-btn" disabled>⏳ 检测中...</button>';
        }
        window.MechPilot.sendCommand('agent.health.check', {});
      });
    }
  }

  function getAllSubmittedJobsForDisplay() {
    return state.submittedJobs.slice();
  }

  function renderRecentTasksHtml() {
    var jobs = getAllSubmittedJobsForDisplay();
    if (jobs.length === 0) {
      return '<div class="recent-placeholder">' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> 属性审核 · 211015932_HRA1标准托盘 <span class="recent-time">14:26</span></div>' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> AI 对话 · "123" <span class="recent-time">14:26</span></div>' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> AI 对话 · "找轴承" <span class="recent-time">14:27</span></div>' +
      '</div>';
    }
    var html = '';
    jobs.slice(0, 5).forEach(function (t) {
      var stCls = t.status === 'completed' ? 'ok' : (t.status === 'failed' ? 'err' : 'warn');
      html += '<div class="recent-row"><span class="recent-status ' + stCls + '">' +
        (stCls === 'ok' ? '✓' : stCls === 'err' ? '✗' : '…') +
        '</span> ' + esc(t.source || t.type || 'task') + ' <span class="recent-time">' +
        (t.submitted_at ? formatTime(t.submitted_at) : '') + '</span></div>';
    });
    return html;
  }

  function computeTaskStats(tasks) {
    var completed = tasks.filter(function (t) { return t.status === 'completed'; }).length;
    var failed = tasks.filter(function (t) { return t.status === 'failed'; }).length;
    var running = tasks.filter(function (t) { return t.status === 'running' || t.status === 'queued'; }).length;
    var total = tasks.length;
    return {
      total: total, completed: completed, failed: failed, running: running,
      successRate: total > 0 ? Math.round(completed / total * 100) + '%' : '0%',
      avgDuration: '-'
    };
  }

  // ══════════════════════════════════════════════════════════
  //  Workspace（任务编排 — 原 overview 迁移）
  // ══════════════════════════════════════════════════════════
  function renderWorkspace(container) {
    var total = state.context && state.context.tree ? countNodes(state.context.tree) - 1 : 0;
    var parts = state.context && state.context.tree ? countParts(state.context.tree) : 0;
    var assy = total - parts;
    var ctx = state.context || {};

    container.innerHTML =
      '<div class="workspace-page">' +
        '<div class="page-title ws-title-bar">' +
          '任务编排 <span class="page-subtitle">' + esc(ctx.fileName || '无激活文档') + '</span>' +
          '<span class="ws-title-info">' +
            '类型: ' + esc(ctx.tree ? ctx.tree.docType || 'assembly' : '-') +
            ' · 节点: ' + total +
          '</span>' +
          '<span class="ws-title-actions">' +
            '<label class="ws-ctx-auto-refresh" title="SW 切换文档时自动刷新上下文">' +
              '<input type="checkbox" id="auto-refresh-toggle"' + (state.settings.autoRefreshContext ? ' checked' : '') + '> 自动刷新' +
            '</label>' +
            '<button class="ws-ctx-refresh" id="manual-refresh-btn" title="手动刷新上下文">🔄 刷新</button>' +
          '</span>' +
        '</div>' +

        '<div class="ws-body">' +
          '<div class="ws-layout">' +
            // Left: Design tree
            '<div class="ws-left">' +
              '<div class="panel-header tree-panel-header">' +
                '<span class="tree-title">设计树</span>' +
                '<span class="tree-mode-btns">' +
                  '<button class="tree-mode-btn' + (state.settings.treeViewMode === 'tree' ? ' active' : '') + '" data-mode="tree" title="树状结构">🌲 树状</button>' +
                  '<button class="tree-mode-btn' + (state.settings.treeViewMode === 'flat' ? ' active' : '') + '" data-mode="flat" title="扁平汇总">📋 扁平</button>' +
                '</span>' +
              '</div>' +
              '<div class="tree-filter-bar" id="tree-filter-bar"></div>' +
              '<div class="design-tree-container" id="design-tree-container"></div>' +
            '</div>' +
            '<div class="ws-resizer" id="ws-resizer"></div>' +
            // Center: Action bar + property workbench + merged task queue
            '<div class="ws-center">' +
              // CKP-004-13: Action bar moved above property workbench (within ws-center)
              '<div class="ws-action-bar" id="ws-action-bar"></div>' +
              '<div class="panel-header">选中零部件属性工作区 <span class="task-count" id="prop-table-count">' + getCheckedPartCount() + ' 个零件</span></div>' +
              '<div class="prop-workbench" id="prop-workbench"></div>' +
              '<div class="task-queue-panel" id="task-queue-panel">' +
                '<div class="panel-header tq-panel-header">任务队列 <span class="task-count" id="tq-total-count">' + getTaskQueueCount() + '</span><span class="tq-filter-bar" id="tq-filter-bar"></span>' +
                  '<button class="tq-collapse-btn" id="tq-collapse-btn" title="折叠">▲</button></div>' +
                '<div class="task-queue-container" id="task-list-container">' + renderTaskQueueHtml() + '</div>' +
              '</div>' +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>';

    // Render design tree
    renderDesignTree(document.getElementById('design-tree-container'));

    // Bind context bar events
    var autoRefreshEl = document.getElementById('auto-refresh-toggle');
    if (autoRefreshEl) {
      autoRefreshEl.addEventListener('change', function () {
        state.settings.autoRefreshContext = this.checked;
        // Notify C# to enable/disable ActiveDocChangeNotify
        window.MechPilot.sendCommand('local.set_auto_refresh', { enabled: this.checked });
        addAIMessage('system', this.checked ? '已开启自动刷新：切换文档时自动更新上下文' : '已关闭自动刷新：请手动点击刷新按钮');
      });
    }
    var manualRefreshEl = document.getElementById('manual-refresh-btn');
    if (manualRefreshEl) {
      manualRefreshEl.addEventListener('click', function () {
        // CKP-004-07: 保存当前选择状态以便恢复
        var snap = createSnapshot('before_refresh');
        var oldSel = state.selectedNode;
        showToast('已保存快照（节点：' + (oldSel ? oldSel.name : '无') + '），正在刷新 SolidWorks 上下文…');
        window.MechPilot.sendCommand('refresh_context', {
          oldNodeId: oldSel ? oldSel.id : null,
          oldNodeName: oldSel ? oldSel.name : null
        });
      });
    }
    // 标题栏中树状/扁平模式按钮绑定——切换后同步 active 状态
    document.querySelectorAll('.tree-mode-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        state.settings.treeViewMode = this.getAttribute('data-mode');
        // 刷新按钮 active 状态
        document.querySelectorAll('.tree-mode-btn').forEach(function (b) {
          b.classList.toggle('active', b.getAttribute('data-mode') === state.settings.treeViewMode);
        });
        refreshDesignTree();
      });
    });

    // Render summary + actions
    renderPropertyWorkbench();
    renderActionBar();
    renderTaskQueueFilters();
    refreshTaskList();
    updateAIHeader();

    // CKP-004-22 P3: 设计树宽度拖拽调整
    var resizer = document.getElementById('ws-resizer');
    var wsLayout = document.querySelector('.ws-layout');
    if (resizer && wsLayout) {
      var isResizing = false;
      var startX, startLeftWidth;

      resizer.addEventListener('mousedown', function (e) {
        isResizing = true;
        startX = e.clientX;
        var match = (wsLayout.style.gridTemplateColumns || '').match(/^(\d+)px/);
        startLeftWidth = match ? parseInt(match[1], 10) : 220;
        resizer.classList.add('active');
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
      });

      document.addEventListener('mousemove', function (e) {
        if (!isResizing) return;
        var dx = e.clientX - startX;
        var newWidth = Math.max(160, Math.min(420, startLeftWidth + dx));
        if (window.innerWidth < 960) { wsLayout.style.gridTemplateColumns = ''; return; }
        wsLayout.style.gridTemplateColumns = newWidth + 'px 4px minmax(400px, 1fr)';
      });

      document.addEventListener('mouseup', function () {
        if (!isResizing) return;
        isResizing = false;
        resizer.classList.remove('active');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
      });
    }

    // CKP-004-22 P1: 任务队列折叠
    var tqCollapseBtn = document.getElementById('tq-collapse-btn');
    if (tqCollapseBtn) {
      tqCollapseBtn.addEventListener('click', function () {
        state.taskQueueCollapsed = !state.taskQueueCollapsed;
        var panel = document.getElementById('task-queue-panel');
        if (panel) panel.classList.toggle('collapsed', state.taskQueueCollapsed);
        this.textContent = state.taskQueueCollapsed ? '▼' : '▲';
        this.title = state.taskQueueCollapsed ? '展开' : '折叠';
      });
    }
  }

  // CKP-004-09/13: Top action bar (now above property workbench inside ws-center)
  function renderActionBar() {
    var bar = document.getElementById('ws-action-bar');
    if (!bar) return;

    var node = state.selectedNode;
    var checkedParts = getCheckedPartCount();
    var canReadProps = hasActiveDocumentContext() || node || checkedParts > 0;

    bar.innerHTML =
      '<button class="ws-action-btn" data-action="read_props"' + (canReadProps ? '' : ' disabled') + '>📖 读取属性</button>' +
      '<button class="ws-action-btn" data-action="check_props"' + ((node || checkedParts > 0) ? '' : ' disabled') + '>🔍 属性检查</button>' +
      '<button class="ws-action-btn primary" data-action="review_props"' + (checkedParts > 0 ? '' : ' disabled') + '>📋 属性审核</button>' +
      '<button class="ws-action-btn" data-action="bom_locate"' + ((node || checkedParts > 0) ? '' : ' disabled') + '>📍 BOM定位</button>' +
      '<button class="ws-action-btn" data-action="ai_analyze"' + ((node || checkedParts > 0) ? '' : ' disabled') + '>🤖 AI分析</button>';
    // CKP-004-22: 删除 action bar 中的 refresh_context 按钮（标题栏已有统一入口）

    bar.querySelectorAll('.ws-action-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var action = this.getAttribute('data-action');
        if (this.disabled) return;
        handleAction(action);
      });
    });
  }

  function renderTaskListHtml() {
    var html = '';

    // Task creation buttons
    html += '<div class="task-create-bar">';
    TASK_TYPES.forEach(function (tt) {
      html += '<button class="task-create-btn" data-task-type="' + tt.type + '" title="创建' + tt.label + '任务草稿">' +
        tt.icon + ' ' + tt.label + '</button>';
    });
    html += '</div>';

    // Task list
    if (state.taskDrafts.length === 0 && state.submittedJobs.length === 0 && !(state.activeJob && !state.activeJob.job_id)) {
      html += '<div class="task-empty">' +
        '<div class="task-empty-text">暂无任务</div>' +
        '<div class="task-empty-hint">点击上方按钮创建任务草稿，选中对象后再创建可绑定对象。</div>' +
      '</div>';
    } else {
      // Drafts
      state.taskDrafts.slice().reverse().forEach(function (d) {
        var isCurrent = d.taskId === state.currentTaskId;
        html += '<div class="task-item task-draft' + (isCurrent ? ' task-current' : '') + '" data-task-id="' + esc(d.taskId) + '">' +
          '<span class="task-icon">📝</span>' +
          '<span class="task-name">' + esc(d.title || d.name || '草稿') + '</span>' +
          '<span class="task-obj-count">' + (d.selectedObjectCount || 0) + '对象</span>' +
          '<span class="task-badge draft">草稿</span></div>';
      });
      // Completed tasks
      getAllSubmittedJobsForDisplay().slice(0, 5).forEach(function (t) {
        var cls = t.status === 'completed' ? 'ok' : (t.status === 'failed' ? 'err' : 'run');
        var badge = t.status === 'completed' ? '完成' : (t.status === 'failed' ? '失败' : '执行中');
        html += '<div class="task-item task-' + cls + '"><span class="task-icon">' + (cls === 'ok' ? '✅' : cls === 'err' ? '❌' : '⏳') +
          '</span><span class="task-name">' + esc(t.source || t.type || 'task') + '</span>' +
          '<span class="task-badge ' + cls + '">' + badge + '</span></div>';
      });
    }
    return html;
  }

  function refreshTaskList() {
    var container = document.getElementById('task-list-container');
    if (!container) return;
    // CKP-004-09/13: use renderTaskQueueHtml (table format with buttons + detail expand)
    container.innerHTML = renderTaskQueueHtml();
    // CKP-004-15: task creation removed from queue (already in top action bar)
    // CKP-004-08/13: Bind task submit buttons
    container.querySelectorAll('.tq-submit').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.stopPropagation();
        submitTaskDraft(this.getAttribute('data-task-id'));
      });
    });
    // CKP-004-08/13: Bind task delete buttons
    container.querySelectorAll('.tq-delete').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.stopPropagation();
        deleteTaskDraft(this.getAttribute('data-task-id'));
      });
    });
    // CKP-004-13: Bind task detail toggle buttons
    container.querySelectorAll('.tq-detail').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.stopPropagation();
        toggleTaskDetail(this.getAttribute('data-task-id'));
      });
    });
    // CKP-004-15: Bind task card click for selection
    container.querySelectorAll('.tq-card[data-task-id]').forEach(function (card) {
      card.addEventListener('click', function (e) {
        if (e.target.closest('button')) return;
        selectTask(this.getAttribute('data-task-id'));
      });
    });
    // CKP-004-13: Update total count
    var countEl = document.getElementById('tq-total-count');
    if (countEl) countEl.textContent = getTaskQueueCount();
  }

  // CKP-004-08: Submit a task draft
  function submitTaskDraft(taskId) {
    var draft = state.taskDrafts.find(function (d) { return d.taskId === taskId; });
    if (!draft) { showToast('任务草稿不存在'); return; }
    if (draft.status !== 'draft') { showToast('该任务已提交，请勿重复提交'); return; }

    switch (draft.taskType) {
      case 'property_review':
        submitPropertyReviewTask(draft);
        break;
      case 'ai_analysis':
        submitAIAnalysisTask(draft);
        break;
      case 'pdm_status_check':
        // CKP-004-08: PDM 状态检查后端未接入
        draft.status = 'not_implemented';
        draft.errorMsg = 'PDM 状态检查提交接口暂未接入';
        persistTaskDrafts();
        refreshTaskList();
        showToast('⚠️ PDM 状态检查提交接口暂未接入');
        break;
      default:
        showToast('未知任务类型：' + draft.taskType);
    }
  }

  // CKP-004-08: Delete a task draft
  function deleteTaskDraft(taskId) {
    var idx = -1;
    for (var i = 0; i < state.taskDrafts.length; i++) {
      if (state.taskDrafts[i].taskId === taskId) { idx = i; break; }
    }
    if (idx < 0) { showToast('任务草稿不存在'); return; }
    var title = state.taskDrafts[idx].title || 'task';
    state.taskDrafts.splice(idx, 1);
    if (state.currentTaskId === taskId) {
      state.currentTaskId = null;
      persistCurrentTask();
      updateAIHeader();
    }
    persistTaskDrafts();
    refreshTaskList();
    showToast('已删除任务草稿：' + esc(title));
  }

  // CKP-004-08: Submit property review task via existing Hermes job chain
  function submitPropertyReviewTask(draft) {
    // Ensure checked nodes match the draft's snapshot
    if (draft.selectedObjectIds && draft.selectedObjectIds.length > 0) {
      state.checkedNodeIds = new Set(draft.selectedObjectIds.filter(function (id) {
        return findNodeById(state.context && state.context.tree, id);
      }));
    }
    selectTask(draft.taskId);

    draft.status = 'submitting';
    draft.submittedAt = new Date().toISOString();
    persistTaskDrafts();
    refreshTaskList();

    var payload = buildJobPayload('material_properties_review', {
      source_action: 'task_draft_submit',
      legacy_command: 'material.properties.review.submit',
      taskId: draft.taskId,
      taskType: draft.taskType,
      snapshotId: draft.snapshotId,
      checkedNodeIds: draft.selectedObjectIds || Array.from(state.checkedNodeIds)
    });

    submitJob('material.properties.review.submit', payload, '属性审核 (草稿提交)');

    // CKP-004-23 P0-2.1: 300s 超时兜底（C# 崩溃不回传时定时器不会泄漏）
    var _guardTimer = null;
    var checkTimer = setInterval(function () {
      if (!state.activeJob || state.activeJob.job_id && isTerminalJobStatus(state.activeJob.status)) {
        clearInterval(checkTimer);
        if (_guardTimer) { clearTimeout(_guardTimer); _guardTimer = null; }
        draft.status = state.activeJob && state.activeJob.status === 'completed' ? 'completed' :
                       state.activeJob && state.activeJob.status === 'partial_failed' ? 'partial_failed' :
                       state.activeJob && state.activeJob.status === 'failed' ? 'failed' : 'submitted';
        draft.submittedJobId = state.activeJob ? state.activeJob.job_id : '';
        if (state.activeJob) {
          if (state.activeJob.submitted_at) draft.submittedAt = state.activeJob.submitted_at;
          if (state.activeJob.started_at) draft.startedAt = state.activeJob.started_at;
          if (state.activeJob.completed_at) draft.completedAt = state.activeJob.completed_at;
        }
        persistTaskDrafts();
        refreshTaskList();
        showToast('属性审核任务' + (draft.status === 'completed' ? '已完成' : draft.status === 'partial_failed' ? '部分失败' : draft.status === 'failed' ? '失败' : '已提交'));
      }
    }, 1000);
    _guardTimer = setTimeout(function () {
      clearInterval(checkTimer);
      draft.status = 'failed';
      draft.errorMsg = '任务超时（无回调）';
      draft.completedAt = new Date().toISOString();
      persistTaskDrafts();
      refreshTaskList();
    }, 300000);
  }

  // CKP-004-08: Submit AI analysis task with taskContext
  function submitAIAnalysisTask(draft) {
    selectTask(draft.taskId);
    draft.status = 'submitting';
    draft.submittedAt = new Date().toISOString();
    persistTaskDrafts();
    refreshTaskList();

    // Populate task context via ai.assistant.chat
    var currentTask = getCurrentTask();
    var selObjNames = draft.selectedObjectNames || [];
    var namesText = selObjNames.length > 0 ? selObjNames.slice(0, 5).join(', ') + (selObjNames.length > 5 ? ' 等' + selObjNames.length + '个' : '') : '当前文档';

    // Set up active job like normal AI chat
    clearJobPollTimer();
    state.activeJob = {
      job_id: '',
      accepted: false,
      status: 'submitting',
      progress_percent: 0,
      current_stage: '提交中',
      source: 'ai.assistant.chat',
      chat_message: 'AI 分析任务: ' + (draft.title || '未命名'),
      submitted_at: new Date().toISOString(),
      started_at: null,
      completed_at: null,
      request_id: null,
      payload: null
    };
    renderJobStatusPanel();
    renderStatusbar();

    var payload = {
      page: state.currentPage,
      message: 'AI 分析任务: 请对以下对象进行分析 ' + namesText,
      context: state.context ? {
        fileName: state.context.fileName,
        filePath: state.context.filePath,
        mode: state.context.mode,
        docType: state.context.tree ? state.context.tree.docType : ''
      } : null,
      contextMode: state.settings.contextMode
    };
    if (currentTask) {
      payload.taskContext = {
        taskId: currentTask.taskId,
        taskType: currentTask.taskType,
        title: currentTask.title,
        selectedObjectIds: currentTask.selectedObjectIds,
        selectedObjectNames: currentTask.selectedObjectNames,
        selectedObjectCount: currentTask.selectedObjectCount,
        aiThreadId: currentTask.aiThreadId
      };
    }

    try {
      state.activeJob.request_id = window.MechPilot.sendCommand('ai.assistant.chat', payload);
      addAIMessage('system', 'AI 分析任务已发送（' + namesText + '），等待 Agent 响应…');
    } catch (e) {
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '提交失败';
      state.activeJob.message = '发送失败: ' + e.message;
      renderJobStatusPanel();
      renderStatusbar();
      addAIMessage('system', state.activeJob.message);
    }

    var _guardTimer2 = null;
    var checkTimer = setInterval(function () {
      if (!state.activeJob || state.activeJob.job_id && isTerminalJobStatus(state.activeJob.status)) {
        clearInterval(checkTimer);
        if (_guardTimer2) { clearTimeout(_guardTimer2); _guardTimer2 = null; }
        draft.status = state.activeJob && (state.activeJob.status === 'completed' || state.activeJob.status === 'partial_failed') ? 'completed' :
                       state.activeJob && state.activeJob.status === 'failed' ? 'failed' : 'submitted';
        draft.submittedJobId = state.activeJob ? state.activeJob.job_id : '';
        persistTaskDrafts();
        refreshTaskList();
        showToast('AI 分析任务' + (draft.status === 'completed' ? '已完成' : draft.status === 'failed' ? '失败' : '已提交'));
      }
    }, 1000);
    _guardTimer2 = setTimeout(function () {
      clearInterval(checkTimer);
      draft.status = 'failed';
      draft.errorMsg = 'AI 分析任务超时（无回调）';
      persistTaskDrafts();
      refreshTaskList();
    }, 300000);
  }

  function updateAIHeader() {
    var header = document.querySelector('.ai-panel-header');
    if (!header) return;
    var task = getCurrentTask();
    var titleEl = header.querySelector('.ai-panel-title');
    if (!titleEl) {
      titleEl = document.createElement('span');
      titleEl.className = 'ai-panel-title';
      header.insertBefore(titleEl, header.firstChild);
    }
    if (task) {
      titleEl.innerHTML = 'AI 对话 <span class="ai-task-binding">📌 ' + esc(task.title || task.taskType) + '</span>';
    } else {
      titleEl.textContent = 'AI 对话（全局工作台对话）';
    }
  }

  function updateOverviewBottomStats() {
    var checkedPartsEl = document.getElementById('stat-checked-parts');
    if (checkedPartsEl) checkedPartsEl.textContent = String(getCheckedPartCount());
  }

  // Default key property names (matches config.json read_property_names)
  var DEFAULT_KEY_PROPERTIES = ['物料编码', '物料名称', '规格型号', '材质', '表面处理', '设计人', '物料状态'];

  // Property field aliases (CKP-004-05 + CKP-004-07 expansion)
  var PROP_ALIASES = {
    '物料编码': ['物料编码', 'W物料编码', 'FileBM', '物料代码', '编码', 'PartNumber', '零件号', 'MaterialCode'],
    '物料名称': ['物料名称', 'W物料名称', '名称', 'Description', 'PartName', '零件名称'],
    '规格型号': ['规格型号', 'G规格型号', '规格', '型号', 'Specification', 'Model'],
    '材质':     ['材质', 'C材质', '材料', 'Material', 'C材料', 'C_Material'],
    '表面处理': ['表面处理', 'SurfaceTreatment', '表面處理', 'Finish', 'Coating'],
    '设计人':   ['设计人', '设计', 'Designer', '设计人员', '设计者', 'DesignedBy', 'Author', '创建者'],
    '物料状态': ['物料状态']
  };

  var PROP_COLUMNS = [
    { key: 'fileName', label: '文件名称', intrinsic: true },
    { key: 'docType', label: '文件类型', intrinsic: true },
    { key: 'instanceCount', label: '实例数', intrinsic: true },
    { key: 'filePath', label: '文件路径', intrinsic: true },
    { key: 'fileSize', label: '文件大小', intrinsic: true },
    { key: '物料编码', label: '物料编码' },
    { key: '物料名称', label: '物料名称' },
    { key: '规格型号', label: '规格型号' },
    { key: '材质', label: '材质' },
    { key: '表面处理', label: '表面处理' },
    { key: '设计人', label: '设计人' },
    { key: '物料状态', label: '物料状态' }
  ];

  function getKeyPropertyNames() {
    var cfg = state.context && state.context._runtimeConfig;
    if (cfg && cfg.read_property_names && Array.isArray(cfg.read_property_names)) {
      return cfg.read_property_names;
    }
    return DEFAULT_KEY_PROPERTIES;
  }

  function mapProps(properties) {
    var mapped = {};
    properties = properties || {};
    Object.keys(properties).forEach(function (key) {
      var value = properties[key] || {};
      mapped[key] = {
        raw: value.RawValue != null ? value.RawValue : (value.rawValue != null ? value.rawValue : value.raw),
        resolved: value.ResolvedValue != null ? value.ResolvedValue : (value.resolvedValue != null ? value.resolvedValue : value.resolved)
      };
    });
    return mapped;
  }

  function mapRowProperties(row) {
    row = row || {};
    var mapped = mapProps(row.Properties || row.properties);
    var flat = row.ResolvedProperties || row.resolvedProperties || {};
    Object.keys(flat).forEach(function (key) {
      var val = flat[key];
      if (val == null || val === '') return;
      if (!mapped[key] || !(mapped[key].resolved || mapped[key].raw)) {
        mapped[key] = { raw: val, resolved: val };
      }
    });
    return mapped;
  }

  // ── Property value resolver with alias support (CKP-004-07 enhanced) ──
  function lookupPropertyRowForNode(node) {
    if (!node || !state.context || !state.context._propertyIndex) return null;
    var idx = state.context._propertyIndex;
    var pivot = node.pivotKey || node.PivotKey || '';
    if (pivot && idx.byPivot && idx.byPivot[pivot]) return idx.byPivot[pivot];
    var fp = normalizeFilePath(node.filePath || node.FilePath || '');
    if (fp && idx.byFilePath && idx.byFilePath[fp]) return idx.byFilePath[fp];
    var comp = (node.componentName || node.ComponentName || node.displayName || node.name || '').trim();
    var cfg = (node.configuration || node.Configuration || '(默认)').trim();
    if (comp && idx.byCompKey) {
      var ck = comp.toLowerCase() + '|' + cfg.toLowerCase();
      if (idx.byCompKey[ck]) return idx.byCompKey[ck];
      var ckDefault = comp.toLowerCase() + '|(默认)';
      if (idx.byCompKey[ckDefault]) return idx.byCompKey[ckDefault];
    }
    return null;
  }

  function resolvePropValue(node, propKey) {
    if (!node) return '';
    // Intrinsic fields
    if (propKey === 'fileName') return node.name || '';
    if (propKey === 'docType') return node.docType || node.type || '';
    if (propKey === 'filePath') {
      var fp = node.filePath || '';
      if (isUsableFilePath(fp)) return fp;
      if (fp === '不可用') return '不可用';
      return fp;
    }
    if (propKey === 'fileSize') return node.fileSize || '';
    // Custom properties with alias lookup (values come from SW custom properties only)
    var aliases = PROP_ALIASES[propKey] || [propKey];
    for (var i = 0; i < aliases.length; i++) {
      var v = node.properties && node.properties[aliases[i]];
      if (v) {
        var display = v.resolved || v.raw || '';
        if (display) return display;
      }
    }
    var row = lookupPropertyRowForNode(node);
    if (row) {
      var rowProps = mapRowProperties(row);
      for (var j = 0; j < aliases.length; j++) {
        var rv = rowProps[aliases[j]];
        if (rv) {
          var rowDisplay = rv.resolved || rv.raw || '';
          if (rowDisplay) return rowDisplay;
        }
      }
    }
    // CKP-004-19 Bug 5: 实例数
    if (propKey === 'instanceCount') return String(node.quantity || 1);
    return '';
  }

  // ── Collect all checked part nodes (deduplicated by filePath) ──
  function getCheckedPartNodes() {
    var nodes = [];
    if (!state.context || !state.context.tree) return nodes;
    var seen = {};
    // 从 tree.children 开始，跳过 root（root properties 为空，属性在子节点）
    if (state.context.tree.children) {
      state.context.tree.children.forEach(function (child) {
        collectCheckedParts(child, nodes, seen);
      });
    }
    return nodes;
  }

  function collectCheckedParts(node, out, seen) {
    if (!node) return;
    seen = seen || {};
    if (isPartNode(node) && state.checkedNodeIds.has(node.id)) {
      var dedupKey = getNodeGroupKey(node);
      if (!seen[dedupKey]) {
        seen[dedupKey] = true;
        out.push(node);
      }
      return; // part 是叶子，不再递归
    }
    if (node.children) {
      node.children.forEach(function (c) { collectCheckedParts(c, out, seen); });
    }
  }

  // CKP-004-19: 收集树中所有 part 节点（属性表空时回退用）
  function collectAllParts(node, out) {
    if (!node) return;
    if (isPartNode(node)) out.push(node);
    if (node.children) {
      node.children.forEach(function (c) { collectAllParts(c, out); });
    }
  }

  // ── Property workbench table (CKP-004-19: only checked parts, no fallback) ──
  function renderPropertyWorkbench() {
    var wb = document.getElementById('prop-workbench');
    if (!wb) return;

    var workspaceItems = getWorkspaceItems();
    var countEl = document.getElementById('prop-table-count');
    if (countEl) countEl.textContent = workspaceItems.length + ' 个零件';

    if (workspaceItems.length === 0) {
      wb.innerHTML = '<div class="prop-empty-info">请在设计树中勾选零件以查看属性，或点击"读取属性"加载 SW 数据。</div>';
      return;
    }

    var html = '<table class="prop-table"><thead><tr>';
    PROP_COLUMNS.forEach(function (col) {
      html += '<th>' + col.label + '</th>';
    });
    html += '</tr></thead><tbody>';

    workspaceItems.forEach(function (item) {
      var node = item.node;
      var isSelected = state.selectedNode && item.nodeIds.indexOf(state.selectedNode.id) >= 0;
      html += '<tr class="prop-row' + (isSelected ? ' prop-selected' : '') + '" data-node-id="' + node.id + '">';
      PROP_COLUMNS.forEach(function (col) {
        var val = resolvePropValue(node, col.key);
        if (col.key === 'instanceCount') val = String(item.instanceCount || node.quantity || 1);
        var display = val || '—';
        var title = col.key === 'filePath' && val ? ' title="' + esc(val) + '"' : '';
        var tdClass = col.key === 'filePath' ? ' class="prop-file-path"' : (val ? '' : ' class="prop-empty"');
        html += '<td' + tdClass + title + '>' + esc(display) + '</td>';
      });
      html += '</tr>';
    });

    html += '</tbody></table>';
    wb.innerHTML = html;

    // Bind row click to select node
    wb.querySelectorAll('.prop-row').forEach(function (row) {
      row.addEventListener('click', function () {
        var nodeId = this.getAttribute('data-node-id');
        var node = findNodeById(state.context.tree, nodeId);
        if (node) {
          state.selectedNode = node;
          renderPropertyWorkbench();
          refreshDesignTree();
          updateAIHeader();
        }
      });
    });
  }

  // ── Task queue (CKP-004-15: 5×2 card grid, 6-col within each card, creation buttons removed) ──
  function renderTaskQueueHtml() {
    var html = '';

    // CKP-004-15: 移除任务创建按钮（顶部 action bar 已有）

    // CKP-004-13: Merge all task entries (drafts + submitted tasks) into unified table
    var allEntries = [];

    // Drafts
    state.taskDrafts.forEach(function (d) {
      allEntries.push({
        isDraft: true,
        taskId: d.taskId,
        title: d.title || d.taskId,
        taskType: d.taskType,
        typeLabel: (TASK_TYPES.find(function(t){return t.type===d.taskType;})||TASK_TYPES[0]).label,
        typeIcon: (TASK_TYPES.find(function(t){return t.type===d.taskType;})||TASK_TYPES[0]).icon,
        objCount: d.selectedObjectCount || 0,
        status: d.status || 'draft',
        queuePos: d.queuePos || d.submittedQueuePos || '',
        submittedJobId: d.submittedJobId || '',
        sortTime: d.submittedAt || d.createdAt,
        times: {
          created_at: d.createdAt,
          submitted_at: d.submittedAt,
          started_at: d.startedAt,
          completed_at: d.completedAt
        },
        draft: d
      });
    });

    // Submitted Hermes jobs (persisted history)
    state.submittedJobs.forEach(function (job) {
      allEntries.push(jobRecordToQueueEntry(job, { isActiveJob: state.activeJob && state.activeJob.job_id === job.job_id }));
    });

    // Pending submit (no job_id yet)
    if (state.activeJob && (!state.activeJob.job_id || findSubmittedJobIndex(state.activeJob.job_id) < 0)) {
      allEntries.push(jobRecordToQueueEntry(state.activeJob, { isActiveJob: true }));
    }

    sortTaskQueueEntries(allEntries);

    // Filter by current tab
    var filter = state.taskQueueFilter || 'all';
    var filtered = allEntries.filter(function (e) {
      if (filter === 'all') return true;
      if (filter === 'active') return e.isActiveJob || e.status === 'running' || e.status === 'submitting';
      if (filter === 'draft') return e.status === 'draft';
      if (filter === 'queued') return e.status === 'queued';
      if (filter === 'completed') return e.status === 'completed' || e.status === 'failed' || e.status === 'partial_failed';
      return true;
    });

    // Assign queue positions
    filtered.forEach(function (e, idx) {
      if (!e.queuePos) e.queuePos = '#' + (idx + 1);
    });

    if (filtered.length === 0) {
      html += '<div class="task-empty"><div class="task-empty-text">' +
        (filter==='draft'?'暂无草稿':filter==='queued'?'无排队中任务':filter==='running'?'无执行中任务':filter==='completed'?'无已完成任务':'暂无任务') +
        '</div></div>';
    } else {
      // CKP-004-15: 5×2 双列卡片网格，每个卡片内 6 列对齐
      html += '<div class="tq-card-grid">';

      filtered.forEach(function (e) {
        var isCurrent = e.taskId === state.currentTaskId;
        var statusCls = e.status === 'draft' ? 'draft' : e.status === 'completed' ? 'ok' : e.status === 'failed' ? 'err' : e.status === 'partial_failed' ? 'warn' : 'run';
        var statusText = e.status === 'draft' ? '草稿' : e.status === 'queued' ? '排队中' : e.status === 'submitting' ? '提交中' : e.status === 'running' ? '执行中' : e.status === 'completed' ? '已完成' : e.status === 'failed' ? '失败' : e.status === 'partial_failed' ? '部分失败' : e.status;
        var canSubmit = e.status === 'draft';
        var canDelete = e.status === 'draft' || e.status === 'queued';
        var canDetail = true;
        var entryTimes = getEntryTimes(e);
        var timeLabel = getTaskQueueTimeLabel(entryTimes, e.status);
        var timeTitle = buildTaskTimeTitle(entryTimes, e.status);

        html += '<div class="tq-card' + (isCurrent ? ' tq-current' : '') + '" data-task-id="' + esc(e.taskId) + '">' +
          '<span class="tq-col-pos">' + esc(e.queuePos) + '</span>' +
          '<span class="tq-col-type" title="' + esc(e.typeLabel || e.taskType) + '">' + (e.typeIcon||'') + ' ' + esc(e.typeLabel || e.taskType) + '</span>' +
          '<span class="tq-col-id" title="' + esc(e.taskId) + '">' + esc(e.taskId) + '</span>' +
          '<span class="tq-col-obj">' + (e.objCount > 0 ? '×' + e.objCount : '-') + '</span>' +
          '<span class="tq-col-time" title="' + esc(timeTitle) + '">' + esc(timeLabel) + '</span>' +
          '<span class="tq-col-status"><span class="tq-badge ' + statusCls + '">' + esc(statusText) + '</span></span>' +
          '<span class="tq-col-act">' +
            (canDetail ? '<button class="tq-btn tq-detail" data-task-id="' + esc(e.taskId) + '">详情</button>' : '') +
            (canSubmit ? '<button class="tq-btn tq-submit" data-task-id="' + esc(e.taskId) + '">提交</button>' : '') +
            (canDelete ? '<button class="tq-btn tq-delete" data-task-id="' + esc(e.taskId) + '">删除</button>' : '') +
          '</span>' +
        '</div>';
      });
      html += '</div>';
    }
    return html;
  }

  // CKP-004-19: Open task detail in modal dialog (not inline expand)
  function toggleTaskDetail(taskId) {
    // Find the entry across drafts + tasks + active job
    var entry = null;
    state.taskDrafts.forEach(function (d) {
      if (d.taskId === taskId) {
        entry = {
          taskId: d.taskId, taskType: d.taskType,
          typeLabel: (TASK_TYPES.find(function(t){return t.type===d.taskType;})||TASK_TYPES[0]).label,
          status: d.status || 'draft', submittedJobId: d.submittedJobId || '',
          objCount: d.selectedObjectCount || 0,
          times: {
            created_at: d.createdAt,
            submitted_at: d.submittedAt,
            started_at: d.startedAt,
            completed_at: d.completedAt
          },
          draft: d, isActiveJob: false
        };
      }
    });
    if (!entry) {
      state.submittedJobs.forEach(function (job) {
        var jid = job.job_id || job.taskId;
        if (jid === taskId) {
          entry = jobRecordToQueueEntry(job, { isActiveJob: state.activeJob && state.activeJob.job_id === jid });
          entry.draft = null;
        }
      });
    }
    if (!entry && state.activeJob && (state.activeJob.job_id === taskId || state.activeJob._pendingId === taskId)) {
      var aj = state.activeJob;
      entry = jobRecordToQueueEntry(aj, { isActiveJob: true });
    }
    if (!entry) { showToast('任务详情不存在'); return; }
    showTaskDetailModal(entry);
  }

  function showTaskDetailModal(e) {
    var statusText = e.status === 'draft' ? '草稿' : e.status === 'queued' ? '排队中' : e.status === 'submitting' ? '提交中' : e.status === 'running' ? '执行中' : e.status === 'local_running' ? '本地执行中' : e.status === 'completed' ? '已完成' : e.status === 'failed' ? '失败' : e.status === 'partial_failed' ? '部分失败' : e.status;
    var statusCls = e.status === 'draft' ? 'draft' : e.status === 'completed' ? 'ok' : e.status === 'failed' ? 'err' : e.status === 'partial_failed' ? 'warn' : 'run';
    var lines = [];
    lines.push('<tr><td>任务ID</td><td>' + esc(e.taskId) + '</td></tr>');
    lines.push('<tr><td>任务类型</td><td>' + esc(e.typeLabel || e.taskType) + '</td></tr>');
    lines.push('<tr><td>状态</td><td><span class="tq-badge ' + statusCls + '">' + esc(statusText) + '</span></td></tr>');
    if (e.submittedJobId) lines.push('<tr><td>Hermes Job ID</td><td>' + esc(e.submittedJobId) + '</td></tr>');
    lines.push('<tr><td>对象数</td><td>' + e.objCount + '</td></tr>');
    buildTaskTimingDetailRows(getEntryTimes(e), e.status).forEach(function (row) {
      lines.push('<tr><td>' + esc(row[0]) + '</td><td>' + esc(row[1]) + '</td></tr>');
    });
    if (e.isActiveJob || e.reviewItems || e.summary || e.submittedJobId) {
      var jobForDetail = getJobById(e.submittedJobId || e.taskId) || (e.isActiveJob ? state.activeJob : null);
      buildAgentReviewDetailRows(jobForDetail || e).forEach(function (row) {
        lines.push('<tr><td>' + esc(row[0]) + '</td><td>' + esc(row[1]) + '</td></tr>');
      });
    }
    if (e.isActiveJob || e.progress != null || e.currentStage || e.jobMessage) {
      lines.push('<tr><td>进度</td><td>' + (e.progress != null ? Math.round(e.progress) + '%' : '-') + '</td></tr>');
      lines.push('<tr><td>完成/总数</td><td>' + (e.completedItems != null ? e.completedItems : '-') + '/' + (e.totalItems || '-') + '</td></tr>');
      lines.push('<tr><td>当前阶段</td><td>' + esc(e.currentStage || '-') + '</td></tr>');
      if (e.jobMessage) lines.push('<tr><td>消息</td><td>' + esc(e.jobMessage) + '</td></tr>');
      if (e.results && Array.isArray(e.results) && e.results.length > 0) {
        var resHtml = e.results.map(function (r) {
          var ok = r.success === true || r.status === 'completed';
          var name = r.component || r.name || r.item_id || '-';
          return '<div class="modal-result ' + (ok ? 'ok' : 'fail') + '">' + (ok ? '✓' : '✗') + ' ' + esc(name) + '</div>';
        }).join('');
        lines.push('<tr><td>结果</td><td>' + resHtml + '</td></tr>');
      }
    }
    if (e.draft) {
      if (e.draft.selectedObjectNames && e.draft.selectedObjectNames.length > 0) {
        lines.push('<tr><td>绑定对象</td><td>' + esc(e.draft.selectedObjectNames.join(', ')) + '</td></tr>');
      }
      if (e.draft.snapshotId) lines.push('<tr><td>快照ID</td><td>' + esc(e.draft.snapshotId) + '</td></tr>');
      if (e.draft.errorMsg) lines.push('<tr><td>错误</td><td><span class="warn">' + esc(e.draft.errorMsg) + '</span></td></tr>');
    }

    var modal = document.getElementById('task-detail-modal');
    if (!modal) {
      modal = document.createElement('div');
      modal.id = 'task-detail-modal';
      modal.className = 'modal-overlay';
      document.body.appendChild(modal);
    }
    modal.innerHTML =
      '<div class="modal-dialog">' +
        '<div class="modal-header"><span class="modal-title">任务详情</span><button class="modal-close" id="modal-close-btn">×</button></div>' +
        '<div class="modal-body"><table class="modal-table"><tbody>' + lines.join('') + '</tbody></table></div>' +
      '</div>';
    modal.style.display = 'flex';
    modal.querySelector('#modal-close-btn').addEventListener('click', function () { modal.style.display = 'none'; });
    modal.addEventListener('click', function (ev) { if (ev.target === modal) modal.style.display = 'none'; });
  }

  // CKP-004-13: Render task queue filter tabs
  function renderTaskQueueFilters() {
    var bar = document.getElementById('tq-filter-bar');
    if (!bar) return;
    var cur = state.taskQueueFilter || 'all';
    var tabs = [
      { key: 'all', label: '全部' },
      { key: 'active', label: '活跃', icon: '⚡' },
      { key: 'draft', label: '草稿', icon: '📝' },
      { key: 'queued', label: '排队中', icon: '⏳' },
      { key: 'completed', label: '已完成', icon: '✅' }
    ];
    bar.innerHTML = tabs.map(function (t) {
      return '<span class="tq-filter-tab' + (cur === t.key ? ' active' : '') + '" data-filter="' + t.key + '">' +
        (t.icon||'') + ' ' + t.label + '</span>';
    }).join('');

    bar.querySelectorAll('.tq-filter-tab').forEach(function (el) {
      el.addEventListener('click', function () {
        state.taskQueueFilter = this.getAttribute('data-filter');
        renderTaskQueueFilters();
        refreshTaskList();
      });
    });
  }

  function updateOverviewSummary() {
    // On workspace page, delegate to property workbench
    if (state.currentPage === 'workspace') {
      renderPropertyWorkbench();
      return;
    }
    var card = document.getElementById('summary-card');
    if (!card) return;

    var node = state.selectedNode;
    var propNames = getKeyPropertyNames();

    // Batch selection summary
    var batchHtml =
      '<div class="summary-batch">' +
        '<span class="batch-chip">已勾选 <b>' + getCheckedNodeCount() + '</b> 项</span>' +
        '<span class="batch-chip">零件 <b>' + getCheckedPartCount() + '</b> 个</span>' +
      '</div>';

    if (!node) {
      card.innerHTML = batchHtml + '<p class="hint">请在设计树中点击一个节点查看详情。</p>';
      return;
    }

    // Intrinsic info table
    var intrinsicRows = [
      { k: '名称', v: node.name },
      { k: '类型', v: node.docType || node.type },
      { k: '数量', v: String(node.quantity || 1) },
      { k: '文件路径', v: node.filePath || '(内置)', title: node.filePath },
      { k: '文件大小', v: node.fileSize || '-' }
    ];
    if (node.isSuppressed) intrinsicRows.push({ k: '状态', v: '抑制', cls: 'warn' });
    if (node.isLightweight) intrinsicRows.push({ k: '状态', v: '轻化', cls: 'warn' });

    var intrinsicTable = '<table class="summary-table"><tbody>';
    intrinsicRows.forEach(function (r) {
      intrinsicTable += '<tr><td class="st-k">' + esc(r.k) + '</td><td class="st-v' + (r.cls ? ' ' + r.cls : '') + '"' +
        (r.title ? ' title="' + esc(r.title) + '"' : '') + '>' + esc(r.v) + '</td></tr>';
    });
    intrinsicTable += '</tbody></table>';

    // Key properties table (from config-defined names)
    var propsTable = '<table class="summary-table summary-props-table"><thead><tr><th>属性</th><th>值</th></tr></thead><tbody>';
    var hasProps = false;
    propNames.forEach(function (key) {
      var v = node.properties && node.properties[key];
      var display = v && v.resolved ? v.resolved : (v && v.raw ? v.raw : '');
      var cls = display ? '' : ' empty';
      propsTable += '<tr><td class="st-k">' + esc(key) + '</td><td class="st-v' + cls + '">' + esc(display || '—') + '</td></tr>';
      if (display) hasProps = true;
    });
    // Also show any extra properties not in the config list
    if (node.properties) {
      Object.keys(node.properties).forEach(function (key) {
        if (propNames.indexOf(key) >= 0) return;  // already shown
        var v = node.properties[key];
        var display = v && v.resolved ? v.resolved : (v && v.raw ? v.raw : '');
        if (display) {
          propsTable += '<tr class="extra-prop"><td class="st-k">' + esc(key) + '</td><td class="st-v">' + esc(display) + '</td></tr>';
          hasProps = true;
        }
      });
    }
    propsTable += '</tbody></table>';

    card.innerHTML =
      batchHtml +
      '<div class="summary-section">' +
        '<div class="summary-section-header">' +
          '<span class="summary-icon">' + (node.type === 'assembly' ? ICONS.assembly : ICONS.part) + '</span>' +
          '<span class="summary-title">' + esc(node.name) + '</span>' +
        '</div>' +
        intrinsicTable +
      '</div>' +
      '<div class="summary-section">' +
        '<div class="summary-section-label">关键属性</div>' +
        propsTable +
      '</div>';
  }

  function renderActionList() {
    var list = document.getElementById('action-list');
    if (!list) return;

    var node = state.selectedNode;
    var checkedParts = getCheckedPartCount();
    var canReadProps = hasActiveDocumentContext() || node || checkedParts > 0;

    list.innerHTML =
      '<button class="action-btn" data-action="read_props"' + (canReadProps ? '' : ' disabled') + '>读取属性</button>' +
      '<button class="action-btn" data-action="check_props"' + (node ? '' : ' disabled') + '>属性检查</button>' +
      '<button class="action-btn primary action-btn-review" data-action="review_props"' + (checkedParts > 0 ? '' : ' disabled') + '>属性审核</button>' +
      '<button class="action-btn" data-action="bom_locate"' + (node ? '' : ' disabled') + '>BOM定位</button>' +
      '<button class="action-btn" data-action="ai_analyze"' + (node ? '' : ' disabled') + '>发送给AI分析</button>' +
      '<button class="action-btn" data-action="refresh_context">刷新上下文</button>';

    list.querySelectorAll('.action-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var action = this.getAttribute('data-action');
        if (this.disabled) return;
        handleAction(action);
      });
    });
  }

  function handleAction(action) {
    var node = state.selectedNode;
    var checkedComponents = getCheckedComponents();
    var primary = node || (checkedComponents.length === 1 ? findNodeById(state.context && state.context.tree, checkedComponents[0].component_id) : null);
    var payload = {
      nodeId: primary ? primary.id : '',
      name: primary ? primary.name : '',
      filePath: primary ? primary.filePath : '',
      checkedNodeIds: Array.from(state.checkedNodeIds),
      components: checkedComponents
    };

    switch (action) {
      case 'read_props':
        window.MechPilot.sendCommand('local.read_properties', { refresh_context: true });
        addAIMessage('system', '已发送读取属性请求，正在从 SolidWorks 刷新属性…');
        break;
      case 'check_props':
        // 本地属性检查/更新
        window.MechPilot.sendCommand('local.properties.check', payload);
        addAIMessage('system', '已发送属性检查请求：' + (primary ? primary.name : (checkedComponents.length + ' item(s)')));
        break;
      case 'review_props':
        if (getCheckedPartCount() === 0) {
          addAIMessage('system', '请至少勾选一个零件后再提交属性审核。');
          return;
        }
        submitJob(
          'material.properties.review.submit',
          buildJobPayload('material_properties_review', {
            source_action: 'review_props',
            legacy_command: 'ai.props.review',
            selectedNode: node ? { id: node.id, name: node.name, filePath: node.filePath } : null,
            checkedNodeIds: Array.from(state.checkedNodeIds)
          }),
          '属性审核'
        );
        break;
      case 'bom_locate':
        window.MechPilot.sendCommand('bom.locate', payload);
        addAIMessage('system', '已发送BOM定位请求：' + (primary ? primary.name : (checkedComponents.length + ' item(s)')));
        break;
      case 'ai_analyze':
        var aiPayload = {
          node: primary ? { id: primary.id, name: primary.name, type: primary.type, filePath: primary.filePath } : null,
          page: 'workspace'
        };
        window.MechPilot.sendCommand('ai.node.analyze', aiPayload);
        addAIMessage('ai', '正在分析选中对象：' + (primary ? primary.name : (checkedComponents.length + ' item(s)')) + '…');
        break;
      case 'refresh_context':
        window.MechPilot.sendCommand('refresh_context', {});
        addAIMessage('system', '已发送刷新上下文请求');
        break;
    }
  }

  // ══════════════════════════════════════════════════════════
  //  其他页面渲染器
  // ══════════════════════════════════════════════════════════

  function renderAssistant(container) {
    container.innerHTML =
      '<div class="page-title">AI助手</div>' +
      '<div class="assistant-intro">' +
        '<p>在此页面可直接与 AI 对话，所有消息会同步到右侧 AI 面板。</p>' +
        '<p>当前选中对象会自动附加到消息上下文。</p>' +
      '</div>' +
      '<div class="chat-clone" id="chat-clone"></div>';

    var box = document.getElementById('chat-clone');
    state.aiMessages.forEach(function (m) {
      var div = document.createElement('div');
      div.className = 'ai-message ai-' + m.role;
      div.innerHTML = '<div class="ai-bubble">' + esc(m.text) + '</div><div class="ai-time">' + formatTime(m.time) + '</div>';
      box.appendChild(div);
    });
  }

  function renderDrawing(container) {
    container.innerHTML =
      '<div class="page-title">图纸审核</div>' +
      '<div class="ai-page">' +
        '<p class="hint">发送当前图纸信息给 AI 进行审核。若 Hermes 未在线，将显示等待提示。</p>' +
        '<div class="form-row">' +
          '<label>审核重点</label>' +
          '<select id="drawing-focus"><option>全项审核</option><option>尺寸公差</option><option>材料标注</option><option>表面粗糙度</option></select>' +
        '</div>' +
        '<div class="form-row">' +
          '<label>补充说明</label>' +
          '<textarea id="drawing-note" rows="3" placeholder="可选：补充图纸背景或关注项…"></textarea>' +
        '</div>' +
        '<div class="form-actions">' +
          '<button class="btn-primary" id="btn-drawing-send">发送审核请求</button>' +
        '</div>' +
        '<div class="result-box" id="drawing-result"></div>' +
      '</div>';

    document.getElementById('btn-drawing-send').addEventListener('click', function () {
      var focus = document.getElementById('drawing-focus').value;
      var note = document.getElementById('drawing-note').value;
      var c = state.context;
      var sel = state.selectedNode;
      var payload = {
        fileName: c ? c.fileName : '(无)',
        filePath: c ? c.filePath : '',
        focus: focus,
        note: note,
        selectedNode: sel ? { id: sel.id, name: sel.name } : null
      };
      window.MechPilot.sendCommand('ai.drawing.review', payload);

      var resultBox = document.getElementById('drawing-result');
      if (isHermesUsable()) {
        resultBox.innerHTML = '<p class="info">已发送审核请求，等待 Agent 返回结果…</p>';
      } else {
        resultBox.innerHTML = '<p class="warn">等待 Agent 服务接入（Hermes 未在线）</p>' +
          '<pre class="debug">' + esc(JSON.stringify(payload, null, 2)) + '</pre>';
      }
    });
  }

  function renderSelection(container) {
    container.innerHTML =
      '<div class="page-title">快速选型</div>' +
      '<div class="ai-page">' +
        '<p class="hint">输入选型条件，AI 将推荐合适的零部件或标准件。</p>' +
        '<div class="form-row">' +
          '<label>选型类别</label>' +
          '<select id="sel-category"><option>轴承</option><option>螺栓</option><option>密封件</option><option>弹簧</option><option>电机</option></select>' +
        '</div>' +
        '<div class="form-row">' +
          '<label>条件 / 规格</label>' +
          '<textarea id="sel-spec" rows="2" placeholder="例如：内径 20mm，转速 3000rpm…"></textarea>' +
        '</div>' +
        '<div class="form-actions">' +
          '<button class="btn-primary" id="btn-sel-send">推荐选型</button>' +
        '</div>' +
        '<div class="result-box" id="sel-result"></div>' +
      '</div>';

    document.getElementById('btn-sel-send').addEventListener('click', function () {
      var cat = document.getElementById('sel-category').value;
      var spec = document.getElementById('sel-spec').value;
      var payload = { category: cat, spec: spec };
      window.MechPilot.sendCommand('ai.selection.recommend', payload);

      var resultBox = document.getElementById('sel-result');
      resultBox.innerHTML =
        '<p class="info">Mock 选型结果（' + esc(cat) + '）：</p>' +
        '<table class="data-table"><thead><tr><th>型号</th><th>规格</th><th>匹配度</th></tr></thead><tbody>' +
        '<tr><td>' + esc(cat) + '-6204</td><td>20×47×14</td><td>98%</td></tr>' +
        '<tr><td>' + esc(cat) + '-6205</td><td>25×52×15</td><td>92%</td></tr>' +
        '<tr><td>' + esc(cat) + '-6206</td><td>30×62×16</td><td>85%</td></tr>' +
        '</tbody></table>';
    });
  }

  // ── 物料检索结果渲染 ──────────────────────────────────
  var latestMaterialResult = null;

  function renderMaterialResults(result) {
    latestMaterialResult = result;
    var resultBox = document.getElementById('mat-result');
    if (!resultBox) return;

    // 失败情况
    if (!result.ok && result.error) {
      var errorCode = result.error.code || 'UNKNOWN_ERROR';
      if (errorCode === 'HINDSIGHT_OFFLINE') {
        resultBox.innerHTML =
          '<div class="error-card error-offline">' +
          '<div class="error-icon">' + ICONS.warning + '</div>' +
          '<div class="error-title">Hindsight 向量检索服务不可用</div>' +
          '<div class="error-desc">请检查服务地址、bank 和数据库索引。</div>' +
          '<div class="error-detail">' + esc(result.error.message || '') + '</div>' +
          '</div>';
      } else {
        resultBox.innerHTML =
          '<div class="error-card">' +
          '<div class="error-icon">' + ICONS.warning + '</div>' +
          '<div class="error-title">检索失败</div>' +
          '<div class="error-desc">' + esc(result.error.message || '未知错误') + '</div>' +
          '</div>';
      }
      return;
    }

    // 成功情况
    var items = extractResultItems(result);

    if (!Array.isArray(items) || items.length === 0) {
      resultBox.innerHTML = '<p class="hint">未找到匹配的物料。</p>';
      return;
    }

    var html = '<table class="data-table rag-table"><thead><tr>' +
               '<th>物料名称</th><th>规格型号</th><th>材料</th><th>供应商</th><th>图号</th><th>相似度</th><th>命中片段</th>' +
               '</tr></thead><tbody>';

    items.forEach(function (item) {
      var name = item.name || item.title || item.material_name || item['物料名称'] || '-';
      var spec = item.spec || item.model || item['规格型号'] || '-';
      var material = item.material || item['材料'] || '-';
      var supplier = item.supplier || item['供应商'] || '-';
      var drawingNo = item.drawing_no || item.drawingNo || item['图号'] || '-';
      var score = item.score != null ? item.score : (item.similarity != null ? item.similarity : '-');
      var snippet = item.snippet || item.content || item.text || '';

      if (typeof score === 'number') {
        score = (score <= 1 ? score * 100 : score).toFixed(1) + '%';
      }

      html += '<tr>' +
              '<td>' + esc(name) + '</td>' +
              '<td>' + esc(spec) + '</td>' +
              '<td>' + esc(material) + '</td>' +
              '<td>' + esc(supplier) + '</td>' +
              '<td>' + esc(drawingNo) + '</td>' +
              '<td class="score-cell">' + esc(score) + '</td>' +
              '<td class="snippet-cell">' + esc(snippet) + '</td>' +
              '</tr>';
    });

    html += '</tbody></table>';
    resultBox.innerHTML = html;
  }

  // ── 物料检索（RAG 支持）──────────────────────────────────
  function renderMaterial(container) {
    var rag = state.settings;

    container.innerHTML =
      '<div class="page-title">物料检索</div>' +
      '<div class="ai-page">' +
        '<div class="rag-status">' +
          '<span class="rag-dot ' + (state.ragOnline ? 'online' : 'offline') + '"></span>' +
          '<span class="rag-label">RAG 服务：' + (state.ragOnline ? '在线' : '离线（使用 Mock）') + '</span>' +
          '<span class="rag-provider">Provider: ' + esc(rag.ragProvider) + '</span>' +
        '</div>' +
        '<div class="rag-config">' +
          '<div class="kv"><span class="k">SQLite 库路径</span><span class="v">' + esc(rag.ragDbPath) + '</span></div>' +
          '<div class="kv"><span class="k">Collection</span><span class="v">' + esc(rag.ragCollection) + '</span></div>' +
          '<div class="kv"><span class="k">Top K</span><span class="v">' + rag.ragTopK + '</span></div>' +
          '<div class="kv"><span class="k">Score Threshold</span><span class="v">' + rag.ragScoreThreshold + '</span></div>' +
        '</div>' +
        '<div class="form-row">' +
          '<label>检索关键词</label>' +
          '<input type="text" id="mat-query" placeholder="例如：不锈钢直线导轨">' +
        '</div>' +
        '<div class="form-row">' +
          '<label>材质筛选</label>' +
          '<input type="text" id="mat-material" placeholder="例如：SUS304">' +
        '</div>' +
        '<div class="form-row">' +
          '<label>Top K</label>' +
          '<input type="number" id="mat-topk" value="' + rag.ragTopK + '" min="1" max="20">' +
        '</div>' +
        '<div class="form-actions">' +
          '<button class="btn-primary" id="btn-mat-search">执行检索</button>' +
        '</div>' +
        '<div class="result-box" id="mat-result"></div>' +
      '</div>';

    document.getElementById('btn-mat-search').addEventListener('click', function () {
      var query = document.getElementById('mat-query').value;
      var material = document.getElementById('mat-material').value;
      var topk = parseInt(document.getElementById('mat-topk').value, 10) || rag.ragTopK;

      var payload = {
        query: query,
        material: material,
        top_k: topk,
        collection: rag.ragCollection,
        score_threshold: rag.ragScoreThreshold
      };

      window.MechPilot.sendCommand('ai.material.search', payload);

      var resultBox = document.getElementById('mat-result');
      var isWebView2 = window.chrome && window.chrome.webview;
      
      if (isWebView2) {
        // WebView2 环境：显示 loading，等待 C# 返回
        resultBox.innerHTML = '<p class="info loading">正在查询 Hindsight...</p>';
      } else {
        // 非 WebView2 环境：显示 Mock 结果
        resultBox.innerHTML =
          '<p class="warn">非 WebView2 环境，显示 Mock 结果：</p>' +
          '<table class="data-table rag-table"><thead><tr><th>物料名称</th><th>规格型号</th><th>材料</th><th>供应商</th><th>图号</th><th>相似度</th><th>命中片段</th></tr></thead><tbody>' +
          '<tr><td>直线导轨 MGN12</td><td>MGN12-400mm</td><td>SUS304</td><td>HIWIN</td><td>MGN-12-400</td><td>92.0%</td><td>不锈钢材质，适用于高精度场合...</td></tr>' +
          '<tr><td>直线导轨 MGN15</td><td>MGN15-600mm</td><td>SUS304</td><td>HIWIN</td><td>MGN-15-600</td><td>88.0%</td><td>承载能力强，适合重载应用...</td></tr>' +
          '<tr><td>不锈钢滑块</td><td>MGN12H</td><td>SUS304</td><td>HIWIN</td><td>MGN-12H</td><td>85.0%</td><td>精密级滑块，预紧可调...</td></tr>' +
          '<tr><td>直线轴承</td><td>LME8UU</td><td>轴承钢</td><td>THK</td><td>LME-8UU</td><td>78.0%</td><td>标准直线轴承，润滑良好...</td></tr>' +
          '</tbody></table>' +
          '<pre class="debug">' + esc(JSON.stringify(payload, null, 2)) + '</pre>';
      }
    });
  }

  function renderDesign(container) {
    container.innerHTML =
      '<div class="page-title">设计计算</div>' +
      '<div class="ai-page">' +
        '<p class="hint">输入设计参数，由 AI 进行工程计算（强度、刚度、热平衡等）。</p>' +
        '<div class="form-row">' +
          '<label>计算类型</label>' +
          '<select id="design-type"><option>强度校核</option><option>刚度计算</option><option>热平衡</option><option>轴承寿命</option><option>螺栓预紧力</option></select>' +
        '</div>' +
        '<div class="form-row">' +
          '<label>输入参数（JSON 或文本）</label>' +
          '<textarea id="design-params" rows="4" placeholder="{ &quot;load&quot;: 10000, &quot;material&quot;: &quot;Q235&quot; }"></textarea>' +
        '</div>' +
        '<div class="form-actions">' +
          '<button class="btn-primary" id="btn-design-send">执行计算</button>' +
        '</div>' +
        '<div class="result-box" id="design-result"></div>' +
      '</div>';

    document.getElementById('btn-design-send').addEventListener('click', function () {
      var type = document.getElementById('design-type').value;
      var params = document.getElementById('design-params').value;
      var payload = { type: type, params: params };
      window.MechPilot.sendCommand('ai.design.calculate', payload);

      var resultBox = document.getElementById('design-result');
      resultBox.innerHTML =
        '<p class="info">Mock 计算结果（' + esc(type) + '）：</p>' +
        '<div class="calc-result">' +
          '<div class="kv"><span class="k">安全系数</span><span class="v">2.4</span></div>' +
          '<div class="kv"><span class="k">最大应力</span><span class="v">142 MPa</span></div>' +
          '<div class="kv"><span class="k">许用应力</span><span class="v">235 MPa</span></div>' +
          '<div class="kv"><span class="k">结论</span><span class="v" style="color:var(--success)">通过</span></div>' +
        '</div>';
    });
  }

  function renderAgent(container) {
    container.innerHTML =
      '<div class="page-title">Agent任务</div>' +
      '<div class="ai-page">' +
        '<p class="hint">查看和管理已提交的 Agent 任务。</p>' +
        '<div class="form-actions">' +
          '<button class="btn-primary" id="btn-agent-refresh">刷新任务列表</button>' +
          '<button class="btn-secondary" id="btn-agent-submit">提交示例任务</button>' +
        '</div>' +
        '<div class="result-box" data-job-status-panel></div>' +
        '<div class="result-box" id="agent-result"></div>' +
      '</div>';

    renderJobStatusPanel();
    renderAgentTasks();

    document.getElementById('btn-agent-refresh').addEventListener('click', function () {
      if (state.activeJob && state.activeJob.job_id && !isTerminalJobStatus(state.activeJob.status)) {
        window.MechPilot.sendCommand('agent.job.poll', { job_id: state.activeJob.job_id });
      } else {
        window.MechPilot.sendCommand('agent.task.poll', {});
      }
      renderAgentTasks();
    });
    document.getElementById('btn-agent-submit').addEventListener('click', function () {
      var task = {
        taskId: 'task-' + Date.now(),
        type: '图纸审核',
        status: 'running',
        progress: 0,
        summary: '新提交的示例任务'
      };
      state.tasks.push(task);
      submitJob(
        'agent.job.submit',
        buildJobPayload('material_properties_review', {
          source_action: 'agent_task_submit',
          legacy_command: 'agent.task.submit',
          task: { type: task.type, summary: task.summary }
        }),
        'Agent 示例任务'
      );
      renderAgentTasks();
    });
  }

  function renderAgentTasks() {
    var box = document.getElementById('agent-result');
    if (!box) return;
    if (state.tasks.length === 0) {
      box.innerHTML = '<p class="hint">暂无任务。点击“提交示例任务”创建一条 Mock 任务。</p>';
      return;
    }
    var html = '<table class="data-table"><thead><tr><th>任务ID</th><th>类型</th><th>状态</th><th>进度</th><th>结果摘要</th></tr></thead><tbody>';
    state.tasks.forEach(function (t) {
      var statusBadge = t.status === 'running' ? '<span class="badge badge-status warning">运行中</span>' :
                        t.status === 'done' ? '<span class="badge badge-status">完成</span>' :
                        '<span class="badge">' + esc(t.status) + '</span>';
      html += '<tr>' +
        '<td>' + esc(t.taskId) + '</td>' +
        '<td>' + esc(t.type) + '</td>' +
        '<td>' + statusBadge + '</td>' +
        '<td><div class="progress-wrap mini"><div class="progress-bar" style="width:' + (t.progress || 0) + '%"></div></div></td>' +
        '<td>' + esc(t.summary || '') + '</td>' +
        '</tr>';
    });
    html += '</tbody></table>';
    box.innerHTML = html;
  }

  // ── 设置（含 RAG 配置）──────────────────────────────────
  function renderSettings(container) {
    var s = state.settings;

    container.innerHTML =
      '<div class="page-title">设置</div>' +
      '<div class="ai-page">' +
        '<div class="card">' +
          '<div class="card-header">系统设置</div>' +
          '<div class="card-body">' +
            '<div class="kv"><span class="k">执行模式</span><span class="v">' + esc(s.executionMode) + '</span></div>' +
            '<div class="kv"><span class="k">Hermes 地址</span><span class="v">' + esc(s.hermesUrl) + '</span></div>' +
            '<div class="kv"><span class="k">Context 模式</span><span class="v">' + esc(s.contextMode) + '</span></div>' +
            '<div class="kv"><span class="k">部署目录</span><span class="v" title="' + esc(s.deployDir) + '">' + esc(s.deployDir) + '</span></div>' +
          '</div>' +
        '</div>' +
        '<div class="card">' +
          '<div class="card-header">RAG 配置</div>' +
          '<div class="card-body">' +
            '<div class="kv"><span class="k">Provider</span><span class="v">' + esc(s.ragProvider) + '</span></div>' +
            '<div class="kv"><span class="k">SQLite 库路径</span><span class="v" title="' + esc(s.ragDbPath) + '">' + esc(s.ragDbPath) + '</span></div>' +
            '<div class="kv"><span class="k">Collection</span><span class="v">' + esc(s.ragCollection) + '</span></div>' +
            '<div class="kv"><span class="k">Top K</span><span class="v">' + s.ragTopK + '</span></div>' +
            '<div class="kv"><span class="k">Score Threshold</span><span class="v">' + s.ragScoreThreshold + '</span></div>' +
            '<div class="kv"><span class="k">RAG 状态</span><span class="v">' + (state.ragOnline ? '<span class="badge badge-status">在线</span>' : '<span class="badge badge-status warning">离线</span>') + '</span></div>' +
          '</div>' +
        '</div>' +
        '<div class="card">' +
          '<div class="card-header">前端状态</div>' +
          '<div class="card-body">' +
            '<div class="kv"><span class="k">当前页面</span><span class="v">' + esc(PAGES[state.currentPage].title) + '</span></div>' +
            '<div class="kv"><span class="k">选中对象</span><span class="v">' + esc(state.selectedNode ? state.selectedNode.name : '(无)') + '</span></div>' +
            '<div class="kv"><span class="k">AI 面板</span><span class="v">' + (state.aiPanelOpen ? '展开' : '收起') + '</span></div>' +
            '<div class="kv"><span class="k">Hermes 在线</span><span class="v">' + hermesStatusLabel(state.hermesStatus.status) + '</span></div>' +
          '</div>' +
        '</div>' +
      '</div>';
  }

  // ══════════════════════════════════════════════════════════
  //  启动
  // ══════════════════════════════════════════════════════════
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
