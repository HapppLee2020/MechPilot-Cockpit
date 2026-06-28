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
    aiPanelOpen: true,
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
    activeJob: null,
    // Context snapshot model (CKP-004-04)
    snapshots: [],
    currentSnapshotId: null,
    taskDrafts: [],
    aiThreads: [],
    currentTaskId: null,       // 当前选中的任务 ID
    taskQueueFilter: 'all',    // CKP-004-13: 'all' | 'draft' | 'queued' | 'running' | 'completed'
    expandedTaskIds: {},       // CKP-004-13: taskId -> true for detail expand
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
    var d = new Date(dt);
    if (isNaN(d.getTime())) return String(dt);
    var pad = function (n) { return n < 10 ? '0' + n : n; };
    return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
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
    var n = (node.type === 'part' || node.docType === '零件') ? 1 : 0;
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
    return node.type === 'part' || node.docType === '零件';
  }

  function isAssemblyNode(node) {
    if (!node) return false;
    return node.type === 'assembly' || node.docType === '装配体' ||
      (node.children && node.children.length > 0);
  }

  function isSubmittableNode(node) {
    if (!node || !isPartNode(node)) return false;
    // TODO: 待 C# 提供 isHidden / isEnvelope / bomExcluded 等字段后再过滤
    return true;
  }

  // CKP-004-18: 统一提取组件显示名
  // 从 C# CockpitTreeNode / PropertyRow 中提取干净的组件显示名
  // 永远不返回完整路径或 PivotKey
  function getCleanDisplayName(node, row) {
    node = node || {};
    row = row || {};
    // 1. 首选 DisplayName / Name（SW 组件名，如 "Bracket-1"）
    var raw = node.DisplayName || node.displayName || node.Name || node.name
           || row.DisplayName || row.displayName || '';
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

  function isDefaultCheckablePart(node) {
    if (!isSubmittableNode(node)) return false;
    if (node.isSuppressed) return false;
    // TODO: 轻化/封套/BOM 排除等待后端字段；当前仅排除 isSuppressed
    return true;
  }

  // CKP-004-09: 节点分组 key（用于扁平视图勾选联动）
  // 同 filePath 的节点属于同一组（同零部件多实例）
  function getNodeGroupKey(node) {
    if (!node) return '';
    // 1. filePath（最精确，包含完整路径）
    if (node.filePath && node.filePath.indexOf('|') < 0) return 'fp:' + node.filePath;
    // 2. 用 cleanNodeName(stripInstance=true) 作为回退 key
    var cleanFlat = cleanNodeName(node.name || '', true);
    if (cleanFlat) return 'name:' + cleanFlat;
    return 'id:' + (node.id || 'unknown');
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

  // CKP-004-09: 切换整个节点组的勾选状态
  function toggleNodeGroupChecked(groupKey, checked) {
    var tree = state.context && state.context.tree;
    if (!tree) return;
    var nodes = findNodesByGroupKey(tree, groupKey);
    nodes.forEach(function (n) {
      if (checked) state.checkedNodeIds.add(n.id);
      else state.checkedNodeIds.delete(n.id);
    });
    refreshCheckedUi();
  }

  // CKP-004-09: 检查节点组是否全选
  function isGroupFullyChecked(groupKey) {
    var tree = state.context && state.context.tree;
    if (!tree) return false;
    var nodes = findNodesByGroupKey(tree, groupKey);
    if (nodes.length === 0) return false;
    return nodes.every(function (n) { return state.checkedNodeIds.has(n.id); });
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
      } else if (isAssemblyNode(n)) {
        if (checked) state.checkedNodeIds.add(n.id);
        else state.checkedNodeIds.delete(n.id);
      }
    });
  }

  function handleNodeCheckToggle(node, checked) {
    if (isAssemblyNode(node) && node.children && node.children.length > 0) {
      setSubtreeChecked(node, checked);
    } else if (isSubmittableNode(node)) {
      // CKP-004-09: 切换整个节点组（同零部件所有实例联动）
      var groupKey = getNodeGroupKey(node);
      toggleNodeGroupChecked(groupKey, checked);
      return; // toggleNodeGroupChecked already calls refreshCheckedUi
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
    var tree = state.context && state.context.tree;
    if (!tree) return [];
    var components = [];
    var seen = {};
    state.checkedNodeIds.forEach(function (id) {
      if (seen[id]) return;
      var node = findNodeById(tree, id);
      if (node && isSubmittableNode(node)) {
        seen[id] = true;
        components.push(buildComponentFromNode(node));
      }
    });
    return components;
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
      output: data.output || fallback.output || ''
    };
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
  var LS_CURRENT_TASK = 'mechpilot.workspace.currentTaskId.v1';
  var MAX_SNAPSHOTS = 30;

  function saveToLS(key, data) {
    try { localStorage.setItem(key, JSON.stringify(data)); } catch (e) { /* quota exceeded */ }
  }
  function loadFromLS(key) {
    try { var v = localStorage.getItem(key); return v ? JSON.parse(v) : null; } catch (e) { return null; }
  }

  function persistSnapshots() { saveToLS(LS_SNAPSHOTS, state.snapshots); }
  function persistTaskDrafts() { saveToLS(LS_TASK_DRAFTS, state.taskDrafts); }
  function persistCurrentTask() { saveToLS(LS_CURRENT_TASK, state.currentTaskId); }

  function loadPersistedState() {
    var snaps = loadFromLS(LS_SNAPSHOTS);
    if (Array.isArray(snaps)) state.snapshots = snaps;
    var drafts = loadFromLS(LS_TASK_DRAFTS);
    if (Array.isArray(drafts)) state.taskDrafts = drafts;
    var curTask = loadFromLS(LS_CURRENT_TASK);
    if (curTask) state.currentTaskId = curTask;
  }

  // ══════════════════════════════════════════════════════════
  //  快照管理 (工作流 A + B)
  // ══════════════════════════════════════════════════════════
  function createSnapshot(reason) {
    var ctx = state.context || {};
    var snap = {
      snapshotId: 'snap-' + Date.now() + '-' + Math.random().toString(36).substr(2, 6),
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
      taskList: JSON.parse(JSON.stringify(state.tasks.slice(-20))),
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
      html += '<div class="snapshot-item" data-snapshot-id="' + snap.snapshotId + '">' +
        '<div class="snapshot-info">' +
          '<span class="snapshot-doc">' + esc(docName) + '</span>' +
          '<span class="snapshot-meta">' + time + ' · ' + taskCount + '任务 · ' + selCount + '选中</span>' +
        '</div>' +
        '<button class="snapshot-restore-btn" data-snapshot-id="' + snap.snapshotId + '">恢复</button>' +
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
      state.tasks = snap.taskList;
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
      taskId: 'task-' + Date.now() + '-' + Math.random().toString(36).substr(2, 6),
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
    clearJobPollTimer();
    if (!jobId) return;
    jobPollTimer = setInterval(function () {
      if (!state.activeJob || state.activeJob.job_id !== jobId || isTerminalJobStatus(state.activeJob.status)) {
        clearJobPollTimer();
        return;
      }
      window.MechPilot.sendCommand('agent.job.poll', { job_id: jobId });
    }, 3000);
  }

  function submitJob(command, payload, sourceLabel) {
    clearJobPollTimer();
    state.activeJob = {
      job_id: '',
      accepted: false,
      status: 'submitting',
      progress_percent: 0,
      current_stage: '提交中',
      source: sourceLabel || command,
      submitted_at: new Date(),
      request_id: null,
      payload: payload
    };
    renderJobStatusPanel();
    renderStatusbar();

    try {
      state.activeJob.request_id = window.MechPilot.sendCommand(command, payload);
      addAIMessage('system', '已提交任务，等待后端受理：' + (sourceLabel || command));
    } catch (e) {
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '提交失败';
      state.activeJob.message = '无法发送任务请求，请检查 WebView2 / Hermes 连接。';
      renderJobStatusPanel();
      renderStatusbar();
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
      clearJobPollTimer();
      renderJobStatusPanel();
      renderStatusbar();
      addAIMessage('system', state.activeJob.message);
      // CKP-004-10: 自动更新 Hermes 状态
      if (/401|403|Unauthorized|未授权/i.test(state.activeJob.message)) updateHermesStatus('auth_required', state.activeJob.message);
      else if (/failed|失败|timeout|超时|offline/i.test(state.activeJob.message)) updateHermesStatus('offline', state.activeJob.message);
      else updateHermesStatus('error', state.activeJob.message);
      return;
    }

    state.activeJob = normalizeJobData(data, state.activeJob);
    state.activeJob.accepted = true;
    state.activeJob.status = state.activeJob.status || 'queued';
    state.activeJob.current_stage = state.activeJob.current_stage || '排队中';
    renderJobStatusPanel();
    renderStatusbar();
    addAIMessage('system', '任务已受理，Job ID：' + state.activeJob.job_id);
    startJobPolling(state.activeJob.job_id);
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

  function handleJobPollResult(result) {
    var data = getResultData(result);
    if (!isCommandSuccess(result)) {
      if (!state.activeJob) state.activeJob = {};
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '轮询失败';
      state.activeJob.message = (result && result.message) || (result && result.error && result.error.message) || '获取任务状态失败，请检查 Hermes 服务。';
      clearJobPollTimer();
      renderJobStatusPanel();
      renderStatusbar();
      addAIMessage('system', state.activeJob.message);
      return;
    }

    state.activeJob = normalizeJobData(data, state.activeJob);
    if (data.results) state.activeJob.results = data.results;
    renderJobStatusPanel();
    renderStatusbar();
    if (isTerminalJobStatus(state.activeJob.status)) {
      clearJobPollTimer();
      // For normal AI chat, show result as AI bubble
      if (state.activeJob.source === 'ai.assistant.chat') {
        if (state.activeJob.status === 'completed' || state.activeJob.status === 'partial_failed') {
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
          if (state.activeJob.results && Array.isArray(state.activeJob.results) && state.activeJob.results.length > 0) {
            var first = state.activeJob.results[0];
            if (first.content) chatReply = first.content;
            else if (first.output) chatReply = first.output;
            else if (first.result) chatReply = typeof first.result === 'string' ? first.result : JSON.stringify(first.result);
          }
          if (!chatReply) chatReply = 'Agent 处理完成（无文本返回），状态：' + statusLabel(state.activeJob.status);
          addAIMessage('ai', chatReply);
        } else {
          addAIMessage('system', '对话处理' + statusLabel(state.activeJob.status) + '：' + (state.activeJob.message || ''));
        }
      } else {
        var summary = '任务完成：' + statusLabel(state.activeJob.status);
        if (state.activeJob.completed_items != null) summary += '，成功 ' + state.activeJob.completed_items;
        if (state.activeJob.failed_items != null && state.activeJob.failed_items > 0) summary += '，失败 ' + state.activeJob.failed_items;
        addAIMessage('system', summary);
      }
    }
  }

  function statusLabel(status) {
    var map = {
      submitting: '提交中',
      accepted: '已受理',
      queued: '排队中',
      running: '运行中',
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
    // CKP-004-10: 如果 base_url 可用但未检测过或仍是 unknown，可激活手动检测提示
    if (state.hermesStatus.status === 'unknown' && state.settings.hermesUrl) {
      // 不自动健康检查，留给用户手动触发；但标记为 ready_to_check
    }
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

    function mapNode(node) {
      var pivot = node.PivotKey || node.pivotKey || node.NodeId || node.nodeId;
      var row = rowByPivot[pivot] || {};
      var nodeId = node.NodeId || node.nodeId || pivot || row.RowKey || row.rowKey || row.DisplayName || row.displayName;
      // CKP-004-18: 用 getCleanDisplayName 代替旧拼接逻辑，禁止路径/PivotKey 作为 UI name
      var cleanName = getCleanDisplayName(node, row);
      return {
        id: nodeId,
        name: cleanName,
        type: node.IsAssembly || node.isAssembly ? 'assembly' : (node.DocType || node.docType || row.DocType || row.docType || 'part'),
        docType: node.DocType || node.docType || row.DocType || row.docType || '',
        quantity: node.Quantity || node.quantity || row.Quantity || row.quantity || 1,
        filePath: node.FilePath || node.filePath || row.FilePath || row.filePath || '',
        fileSize: row.FileSize || row.fileSize || node.FileSize || node.fileSize || '',
        isSuppressed: node.IsSuppressed || node.isSuppressed || false,
        isLightweight: node.IsLightweight || node.isLightweight || false,
        depth: node.Depth || node.depth || 0,
        childrenCount: node.ChildrenCount || node.childrenCount || 0,
        properties: mapProps(row.Properties || row.properties),
        children: (node.Children || node.children || []).map(mapNode)
      };
    }

    var rootChildren = assemblyTree.map(mapNode);
    if (rootChildren.length === 0) {
      rootChildren = sourceRows.map(function (row, index) {
        var id = row.PivotKey || row.pivotKey || row.RowKey || row.rowKey || ('row-' + index);
        // CKP-004-18: 禁止 PivotKey/filePath 作为 UI name
        var cleanName = getCleanDisplayName({}, row);
        return {
          id: id,
          name: cleanName,
          type: row.DocType || row.docType || 'part',
          docType: row.DocType || row.docType || '',
          quantity: row.Quantity || row.quantity || 1,
          filePath: row.FilePath || row.filePath || '',
          fileSize: row.FileSize || row.fileSize || '',
          properties: mapProps(row.Properties || row.properties),
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
      _isMock: false
    };
  }

  // ══════════════════════════════════════════════════════════
  //  初始化
  // ══════════════════════════════════════════════════════════
  function init() {
    loadPersistedState();  // Restore snapshots, task drafts, current task from localStorage
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
  function injectBridge() {
    window.MechPilot = {
      receiveContext: function (context) {
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
        } else if (cmd === 'agent.job.poll' || (!cmd && state.activeJob && state.activeJob.job_id && (data.job_id === state.activeJob.job_id || data.jobId === state.activeJob.job_id))) {
          handleJobPollResult(result);
        } else if (cmd === 'refresh_context' || (!cmd && data && data.context && result.success)) {
          // Apply refreshed context from C# (cmd may be undefined — MakeCockpitResult has no 'command' field)
          if (data && data.context) {
            window.MechPilot.receiveContext(data.context);
            var fileName = data.context.fileName || (data.context.ActiveDocument && data.context.ActiveDocument.Title) || '';
            showToast('上下文已刷新：' + fileName + '（历史选择已保存在快照中）');
          } else if (result.ok) {
            showToast('上下文刷新完成');
          } else {
            showToast('上下文刷新失败：' + (result.message || ''));
          }
        } else if (cmd === 'local.read_properties') {
          // Fallback: local.read_properties and refresh_context both return { data: { context } },
          // so the (!cmd && data.context) branch above already handles both. This case only
          // fires when the result somehow carries the command name (rare single-path).
          if (data && data.context) {
            window.MechPilot.receiveContext(data.context);
            var propFileName = data.context.fileName || (data.context.ActiveDocument && data.context.ActiveDocument.Title) || '';
            showToast('属性读取完成：' + propFileName + '，已刷新属性表');
          } else if (result.ok) {
            showToast('属性读取完成');
          } else {
            showToast('属性读取失败：' + (result.message || ''));
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
        var requestId = 'req-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
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
          console.log('[MechPilot] sendCommand (mock):', type, payload);
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
        '<span><span class="label">文件：</span><span class="value" id="tb-file">' + esc(c ? c.fileName : '(无)') + '</span></span>' +
        '<span><span class="label">路径：</span><span class="value" id="tb-path" title="' + esc(c ? c.filePath : '') + '">' + esc(c ? c.filePath : '') + '</span></span>' +
      '</div>' +
      '<div class="topbar-right">' +
        // CKP-004-08: 钉住/置顶按钮
        '<button class="topbar-pin-btn' + (state.windowPinned ? ' pinned' : '') + '" id="topbar-pin-btn" title="' + (state.windowPinned ? '已钉住 — 点击取消' : '钉住 — 窗口保持前台') + '">' +
          (state.windowPinned ? '📌 已钉住' : '📌 钉住') +
        '</button>' +
        '<span class="badge badge-mode" id="tb-mode">' + esc(c ? c.mode : 'local') + '</span>' +
        '<span class="badge badge-status" id="tb-status">' + esc(c ? c.status : '演示数据') + '</span>' +
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
    PAGES[pageId].render(container);
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
      submitted_at: new Date(),
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

    // Mode toggle toolbar
    var toolbar = document.createElement('div');
    toolbar.className = 'tree-mode-toolbar';
    toolbar.innerHTML =
      '<button class="tree-mode-btn' + (state.settings.treeViewMode === 'tree' ? ' active' : '') + '" data-mode="tree" title="树状结构">🌲 树状</button>' +
      '<button class="tree-mode-btn' + (state.settings.treeViewMode === 'flat' ? ' active' : '') + '" data-mode="flat" title="扁平汇总">📋 扁平</button>';
    toolbar.querySelectorAll('.tree-mode-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        state.settings.treeViewMode = this.getAttribute('data-mode');
        refreshDesignTree();
      });
    });
    container.appendChild(toolbar);

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
    var parts = allNodes.filter(function (n) { return n.type === 'part'; });
    var assemblies = allNodes.filter(function (n) { return n.type === 'assembly'; });

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
      title.textContent = groupLabel + ' (' + Object.keys(docMap).length + ')';
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
        var someChecked = entry.ids.some(function (id) { return state.checkedNodeIds.has(id); });
        var indeterminate = !allChecked && someChecked;

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
          '<span class="flat-col-name" title="' + esc(displayName) + '">' + esc(displayName) + badges + '</span>' +
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

    var hasChildren = !isPartNode(node) && node.children && node.children.length > 0;
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

    // 图标
    var icon = document.createElement('span');
    icon.className = 'tree-icon';
    icon.innerHTML = node.type === 'assembly' ? ICONS.assembly : ICONS.part;
    row.appendChild(icon);

    // 名称
    var name = document.createElement('span');
    name.className = 'tree-name';
    // CKP-004-15: 去掉文件扩展名 (.SLDPRT 等)
    name.textContent = cleanNodeName(node.name, false);
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
        var newChecked = !state.checkedNodeIds.has(node.id);
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
      node.children.forEach(function (child) {
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
    var mockStats = state.tasks.length > 0 ? computeTaskStats(state.tasks) : {
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

  function renderRecentTasksHtml() {
    if (state.tasks.length === 0) {
      return '<div class="recent-placeholder">' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> 属性审核 · 211015932_HRA1标准托盘 <span class="recent-time">14:26</span></div>' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> AI 对话 · "123" <span class="recent-time">14:26</span></div>' +
        '<div class="recent-row"><span class="recent-status ok">✓</span> AI 对话 · "找轴承" <span class="recent-time">14:27</span></div>' +
      '</div>';
    }
    var html = '';
    var recent = state.tasks.slice(-5).reverse();
    recent.forEach(function (t) {
      var stCls = t.status === 'completed' ? 'ok' : (t.status === 'failed' ? 'err' : 'warn');
      html += '<div class="recent-row"><span class="recent-status ' + stCls + '">' +
        (stCls === 'ok' ? '✓' : stCls === 'err' ? '✗' : '…') +
        '</span> ' + esc(t.type || t.source || 'task') + ' <span class="recent-time">' +
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
        '<div class="page-title">任务编排 <span class="page-subtitle">' + esc(ctx.fileName || '无激活文档') + '</span></div>' +

        // Context bar
        '<div class="ws-context-bar">' +
          '<span class="ws-ctx-item">📄 ' + esc(ctx.fileName || '-') + '</span>' +
          '<span class="ws-ctx-item">类型: ' + esc(ctx.tree ? ctx.tree.docType || 'assembly' : '-') + '</span>' +
          '<span class="ws-ctx-item">节点: ' + total + '</span>' +
          '<label class="ws-ctx-auto-refresh" title="SW 切换文档时自动刷新上下文">' +
            '<input type="checkbox" id="auto-refresh-toggle"' + (state.settings.autoRefreshContext ? ' checked' : '') + '> 自动刷新' +
          '</label>' +
          '<button class="ws-ctx-refresh" id="manual-refresh-btn" title="手动刷新上下文">🔄 刷新</button>' +
        '</div>' +

        '<div class="ws-body">' +
          '<div class="ws-layout">' +
            // Left: Design tree
            '<div class="ws-left">' +
              '<div class="panel-header">设计树 <button class="tree-refresh-btn" id="tree-refresh-btn" title="刷新设计树">↻</button></div>' +
              '<div class="design-tree-container" id="design-tree-container"></div>' +
            '</div>' +

            // Center: Action bar + property workbench + merged task queue
            '<div class="ws-center">' +
              // CKP-004-13: Action bar moved above property workbench (within ws-center)
              '<div class="ws-action-bar" id="ws-action-bar"></div>' +
              '<div class="panel-header">选中零部件属性工作区 <span class="task-count" id="prop-table-count">' + getCheckedPartCount() + ' 个零件</span></div>' +
              '<div class="prop-workbench" id="prop-workbench"></div>' +
              '<div class="task-queue-panel" id="task-queue-panel">' +
                '<div class="panel-header tq-panel-header">任务队列 <span class="task-count" id="tq-total-count">' + (state.taskDrafts.length + state.tasks.length) + '</span><span class="tq-filter-bar" id="tq-filter-bar"></span></div>' +
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
    var treeRefreshEl = document.getElementById('tree-refresh-btn');
    if (treeRefreshEl) {
      treeRefreshEl.addEventListener('click', function () {
        // CKP-004-07: 设计树刷新也向 C# 请求最新上下文
        var snap = createSnapshot('before_tree_refresh');
        var oldSel = state.selectedNode;
        showToast('已保存快照（节点：' + (oldSel ? oldSel.name : '无') + '），正在刷新设计树…');
        window.MechPilot.sendCommand('refresh_context', {
          oldNodeId: oldSel ? oldSel.id : null,
          oldNodeName: oldSel ? oldSel.name : null
        });
      });
    }

    // Render summary + actions
    renderPropertyWorkbench();
    renderActionBar();
    renderTaskQueueFilters();
    refreshTaskList();
    updateAIHeader();
  }

  // CKP-004-09/13: Top action bar (now above property workbench inside ws-center)
  function renderActionBar() {
    var bar = document.getElementById('ws-action-bar');
    if (!bar) return;

    var node = state.selectedNode;
    var checkedParts = getCheckedPartCount();

    bar.innerHTML =
      '<button class="ws-action-btn" data-action="read_props"' + (node ? '' : ' disabled') + '>📖 读取属性</button>' +
      '<button class="ws-action-btn" data-action="check_props"' + (node ? '' : ' disabled') + '>🔍 属性检查</button>' +
      '<button class="ws-action-btn primary" data-action="review_props"' + (checkedParts > 0 ? '' : ' disabled') + '>📋 属性审核</button>' +
      '<button class="ws-action-btn" data-action="bom_locate"' + (node ? '' : ' disabled') + '>📍 BOM定位</button>' +
      '<button class="ws-action-btn" data-action="ai_analyze"' + (node ? '' : ' disabled') + '>🤖 AI分析</button>' +
      '<button class="ws-action-btn" data-action="refresh_context">🔄 刷新上下文</button>';

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
    if (state.taskDrafts.length === 0 && state.tasks.length === 0) {
      html += '<div class="task-empty">' +
        '<div class="task-empty-text">暂无任务</div>' +
        '<div class="task-empty-hint">点击上方按钮创建任务草稿，选中对象后再创建可绑定对象。</div>' +
      '</div>';
    } else {
      // Drafts
      state.taskDrafts.slice().reverse().forEach(function (d) {
        var isCurrent = d.taskId === state.currentTaskId;
        html += '<div class="task-item task-draft' + (isCurrent ? ' task-current' : '') + '" data-task-id="' + d.taskId + '">' +
          '<span class="task-icon">📝</span>' +
          '<span class="task-name">' + esc(d.title || d.name || '草稿') + '</span>' +
          '<span class="task-obj-count">' + (d.selectedObjectCount || 0) + '对象</span>' +
          '<span class="task-badge draft">草稿</span></div>';
      });
      // Completed tasks
      state.tasks.slice(-5).reverse().forEach(function (t) {
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
    if (countEl) countEl.textContent = state.taskDrafts.length + state.tasks.length;
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

    // Monitor job completion to update draft status
    var checkTimer = setInterval(function () {
      if (!state.activeJob || state.activeJob.job_id && isTerminalJobStatus(state.activeJob.status)) {
        clearInterval(checkTimer);
        draft.status = state.activeJob && state.activeJob.status === 'completed' ? 'completed' :
                       state.activeJob && state.activeJob.status === 'partial_failed' ? 'partial_failed' :
                       state.activeJob && state.activeJob.status === 'failed' ? 'failed' : 'submitted';
        draft.submittedJobId = state.activeJob ? state.activeJob.job_id : '';
        persistTaskDrafts();
        refreshTaskList();
        showToast('属性审核任务' + (draft.status === 'completed' ? '已完成' : draft.status === 'partial_failed' ? '部分失败' : draft.status === 'failed' ? '失败' : '已提交'));
      }
    }, 1000);
  }

  // CKP-004-08: Submit AI analysis task with taskContext
  function submitAIAnalysisTask(draft) {
    selectTask(draft.taskId);
    draft.status = 'submitting';
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
      submitted_at: new Date(),
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

    // Monitor job to update draft status
    var checkTimer = setInterval(function () {
      if (!state.activeJob || state.activeJob.job_id && isTerminalJobStatus(state.activeJob.status)) {
        clearInterval(checkTimer);
        draft.status = state.activeJob && (state.activeJob.status === 'completed' || state.activeJob.status === 'partial_failed') ? 'completed' :
                       state.activeJob && state.activeJob.status === 'failed' ? 'failed' : 'submitted';
        draft.submittedJobId = state.activeJob ? state.activeJob.job_id : '';
        persistTaskDrafts();
        refreshTaskList();
        showToast('AI 分析任务' + (draft.status === 'completed' ? '已完成' : draft.status === 'failed' ? '失败' : '已提交'));
      }
    }, 1000);
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
  var DEFAULT_KEY_PROPERTIES = ['物料名称', '图号', '材料', '重量', '表面处理', '处理状态', '处理人', '处理日期'];

  // Property field aliases (CKP-004-05 + CKP-004-07 expansion)
  var PROP_ALIASES = {
    '物料编码': ['物料编码', 'W物料编码', 'FileBM', '物料代码', '编码', 'PartNumber', '零件号', 'MaterialCode'],
    '物料名称': ['物料名称', 'W物料名称', '名称', 'Description', 'PartName', '零件名称'],
    '规格型号': ['规格型号', 'G规格型号', '规格', '型号', 'Specification', 'Model'],
    '材料':     ['材料', 'C材质', '材质', 'Material', 'C材料', 'C_Material'],
    '表面处理': ['表面处理', 'SurfaceTreatment', '表面處理', 'Finish', 'Coating'],
    '处理状态': ['处理状态', 'Status', 'WorkflowState', 'State', '流程状态', '审核状态'],
    '处理人':   ['处理人', 'Handler', 'Auditor', 'Operator', '审核人', '操作人', 'CheckedBy'],
    '处理日期': ['处理日期', 'HandleDate', 'Date', 'AuditDate', 'CheckDate', '审核日期', '操作日期']
  };

  var PROP_COLUMNS = [
    { key: 'fileName', label: '文件名称', intrinsic: true },
    { key: 'docType', label: '文件类型', intrinsic: true },
    { key: 'filePath', label: '文件路径', intrinsic: true },
    { key: 'fileSize', label: '文件大小', intrinsic: true },
    { key: '物料编码', label: '物料编码' },
    { key: '物料名称', label: '物料名称' },
    { key: '规格型号', label: '规格型号' },
    { key: '材料', label: '材料' },
    { key: '表面处理', label: '表面处理' },
    { key: '处理状态', label: '处理状态' },
    { key: '处理人', label: '处理人' },
    { key: '处理日期', label: '处理日期' }
  ];

  function getKeyPropertyNames() {
    var cfg = state.context && state.context._runtimeConfig;
    if (cfg && cfg.read_property_names && Array.isArray(cfg.read_property_names)) {
      return cfg.read_property_names;
    }
    return DEFAULT_KEY_PROPERTIES;
  }

  // ── Property value resolver with alias support (CKP-004-07 enhanced) ──
  function resolvePropValue(node, propKey) {
    if (!node) return '';
    // Intrinsic fields
    if (propKey === 'fileName') return node.name || '';
    if (propKey === 'docType') return node.docType || node.type || '';
    if (propKey === 'filePath') return node.filePath || '';
    if (propKey === 'fileSize') return node.fileSize || '';
    // Custom properties with alias lookup
    var aliases = PROP_ALIASES[propKey] || [propKey];
    for (var i = 0; i < aliases.length; i++) {
      var v = node.properties && node.properties[aliases[i]];
      if (v) {
        var display = v.resolved || v.raw || '';
        if (display) return display;
      }
    }
    // CKP-004-07: 尝试从 name/filePath 解析更多信息
    // 对于物料编码/名称，节点 name 可能包含有用信息
    if (propKey === '物料名称' && node.name) {
      // 去扩展名
      var nameNoExt = node.name.replace(/\.(SLDPRT|SLDASM|SLDDRW|sldprt|sldasm|slddrw)$/i, '');
      if (nameNoExt !== node.name) return nameNoExt;
    }
    return '';
  }

  // ── Collect all checked part nodes ──
  function getCheckedPartNodes() {
    var nodes = [];
    if (!state.context || !state.context.tree) return nodes;
    collectCheckedParts(state.context.tree, nodes);
    return nodes;
  }

  function collectCheckedParts(node, out) {
    if (!node) return;
    if (state.checkedNodeIds.has(node.id) && isPartNode(node)) {
      out.push(node);
    }
    if (node.children) {
      node.children.forEach(function (c) { collectCheckedParts(c, out); });
    }
  }

  // ── Property workbench table (CKP-004-09: show selected node when 0 checked) ──
  function renderPropertyWorkbench() {
    var wb = document.getElementById('prop-workbench');
    if (!wb) return;

    var partNodes = getCheckedPartNodes();
    // CKP-004-09: 如果没有勾选零件，尝试展示当前选中节点
    if (partNodes.length === 0 && state.selectedNode && state.selectedNode.id !== 'root') {
      var sn = state.selectedNode;
      if (isPartNode(sn)) {
        partNodes = [sn];
      } else if (sn.children && sn.children.length > 0) {
        // 选中装配体时，展示其直接子零件
        partNodes = [];
        (sn.children || []).forEach(function (c) {
          if (isPartNode(c)) partNodes.push(c);
        });
      }
    }
    var countEl = document.getElementById('prop-table-count');
    if (countEl) countEl.textContent = partNodes.length + ' 个零件';

    if (partNodes.length === 0) {
      wb.innerHTML = '<div class="prop-empty-info">请在设计树中勾选零件以查看属性，或点击"刷新上下文"加载 SW 数据。</div>';
      return;
    }

    var html = '<table class="prop-table"><thead><tr>';
    PROP_COLUMNS.forEach(function (col) {
      html += '<th>' + col.label + '</th>';
    });
    html += '</tr></thead><tbody>';

    partNodes.forEach(function (node, idx) {
      var isSelected = state.selectedNode && state.selectedNode.id === node.id;
      html += '<tr class="prop-row' + (isSelected ? ' prop-selected' : '') + '" data-node-id="' + node.id + '">';
      PROP_COLUMNS.forEach(function (col) {
        var val = resolvePropValue(node, col.key);
        var cls = val ? '' : ' class="prop-empty"';
        var display = val || '—';
        var title = col.key === 'filePath' ? ' title="' + esc(val) + '"' : '';
        html += '<td' + cls + title + '>' + esc(display) + '</td>';
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
        draft: d
      });
    });

    // Submitted tasks (from state.tasks + active job)
    state.tasks.forEach(function (t, idx) {
      allEntries.push({
        isDraft: false,
        taskId: t.taskId || t.job_id || ('task-' + idx),
        title: t.source || t.type || t.title || 'task',
        taskType: t.type || t.taskType || '',
        typeLabel: t.type || t.taskType || '',
        typeIcon: '📤',
        objCount: t.total_items || t.components ? t.components.length : 0,
        status: t.status || 'unknown',
        queuePos: t.queue_position != null ? '#' + (Number(t.queue_position)+1) : '',
        submittedJobId: t.job_id || t.submittedJobId || '',
        draft: null
      });
    });

    // Active job also shows as entry with progress + results
    var aj = state.activeJob;
    if (aj && !allEntries.some(function(e){return e.submittedJobId===aj.job_id&&aj.job_id;})){
      var ajProgress = Number(aj.progress_percent || 0);
      var ajCompleted = aj.completed_items != null ? aj.completed_items : 0;
      var ajTotal = aj.total_items != null ? aj.total_items : (aj.payload&&aj.payload.components?aj.payload.components.length:0);
      allEntries.push({
        isDraft: false,
        isActiveJob: true,
        taskId: aj.job_id || ('active-' + Date.now()),
        title: aj.source || '当前任务',
        taskType: aj.source || '',
        typeLabel: aj.source || '',
        typeIcon: aj.status==='running'?'⚡':(aj.status==='completed'?'✅':'⏳'),
        objCount: ajTotal,
        status: aj.status || 'running',
        queuePos: aj.queue_position != null ? '#' + (Number(aj.queue_position)+1) : '',
        submittedJobId: aj.job_id || '',
        progress: ajProgress,
        completedItems: ajCompleted,
        totalItems: ajTotal,
        currentStage: aj.current_stage || '',
        jobMessage: aj.message || '',
        results: aj.results || null,
        draft: null
      });
    }

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

        html += '<div class="tq-card' + (isCurrent ? ' tq-current' : '') + '" data-task-id="' + e.taskId + '">' +
          '<span class="tq-col-pos">' + esc(e.queuePos) + '</span>' +
          '<span class="tq-col-type" title="' + esc(e.typeLabel || e.taskType) + '">' + (e.typeIcon||'') + ' ' + esc(e.typeLabel || e.taskType) + '</span>' +
          '<span class="tq-col-id" title="' + esc(e.taskId) + '">' + esc(e.taskId) + '</span>' +
          '<span class="tq-col-obj">' + (e.objCount > 0 ? '×' + e.objCount : '-') + '</span>' +
          '<span class="tq-col-status"><span class="tq-badge ' + statusCls + '">' + esc(statusText) + '</span></span>' +
          '<span class="tq-col-act">' +
            (canDetail ? '<button class="tq-btn tq-detail" data-task-id="' + e.taskId + '">详情</button>' : '') +
            (canSubmit ? '<button class="tq-btn tq-submit" data-task-id="' + e.taskId + '">提交</button>' : '') +
            (canDelete ? '<button class="tq-btn tq-delete" data-task-id="' + e.taskId + '">删除</button>' : '') +
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
          objCount: d.selectedObjectCount || 0, draft: d, isActiveJob: false
        };
      }
    });
    if (!entry) {
      state.tasks.forEach(function (t) {
        if ((t.taskId || t.job_id) === taskId) {
          entry = {
            taskId: t.taskId || t.job_id, taskType: t.type || t.taskType || '',
            typeLabel: t.type || t.taskType || '', status: t.status || 'unknown',
            submittedJobId: t.job_id || '', objCount: t.total_items || 0, draft: null, isActiveJob: false
          };
        }
      });
    }
    if (!entry && state.activeJob && state.activeJob.job_id === taskId) {
      var aj = state.activeJob;
      entry = {
        taskId: aj.job_id, taskType: aj.source || '', typeLabel: aj.source || '',
        status: aj.status || 'running', submittedJobId: aj.job_id,
        objCount: aj.total_items || 0, draft: null, isActiveJob: true,
        progress: aj.progress_percent, completedItems: aj.completed_items,
        totalItems: aj.total_items, currentStage: aj.current_stage,
        jobMessage: aj.message, results: aj.results
      };
    }
    if (!entry) { showToast('任务详情不存在'); return; }
    showTaskDetailModal(entry);
  }

  function showTaskDetailModal(e) {
    var statusText = e.status === 'draft' ? '草稿' : e.status === 'queued' ? '排队中' : e.status === 'submitting' ? '提交中' : e.status === 'running' ? '执行中' : e.status === 'completed' ? '已完成' : e.status === 'failed' ? '失败' : e.status === 'partial_failed' ? '部分失败' : e.status;
    var statusCls = e.status === 'draft' ? 'draft' : e.status === 'completed' ? 'ok' : e.status === 'failed' ? 'err' : e.status === 'partial_failed' ? 'warn' : 'run';
    var lines = [];
    lines.push('<tr><td>任务ID</td><td>' + esc(e.taskId) + '</td></tr>');
    lines.push('<tr><td>任务类型</td><td>' + esc(e.typeLabel || e.taskType) + '</td></tr>');
    lines.push('<tr><td>状态</td><td><span class="tq-badge ' + statusCls + '">' + esc(statusText) + '</span></td></tr>');
    if (e.submittedJobId) lines.push('<tr><td>Hermes Job ID</td><td>' + esc(e.submittedJobId) + '</td></tr>');
    lines.push('<tr><td>对象数</td><td>' + e.objCount + '</td></tr>');
    if (e.isActiveJob) {
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
      if (e.draft.createdAt) lines.push('<tr><td>创建时间</td><td>' + esc(e.draft.createdAt) + '</td></tr>');
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

    list.innerHTML =
      '<button class="action-btn" data-action="read_props"' + (node ? '' : ' disabled') + '>读取属性</button>' +
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
    var payload = node ? { nodeId: node.id, name: node.name, filePath: node.filePath } : {};

    switch (action) {
      case 'read_props':
        window.MechPilot.sendCommand('local.read_properties', payload);
        addAIMessage('system', '已发送读取属性请求：' + (node ? node.name : ''));
        break;
      case 'check_props':
        // 本地属性检查/更新
        window.MechPilot.sendCommand('local.properties.check', payload);
        addAIMessage('system', '已发送属性检查请求：' + (node ? node.name : ''));
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
        addAIMessage('system', '已发送BOM定位请求：' + (node ? node.name : ''));
        break;
      case 'ai_analyze':
        var aiPayload = {
          node: node ? { id: node.id, name: node.name, type: node.type, filePath: node.filePath } : null,
          page: 'workspace'
        };
        window.MechPilot.sendCommand('ai.node.analyze', aiPayload);
        addAIMessage('ai', '正在分析选中对象：' + (node ? node.name : '') + '…');
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
        score = (score * 100).toFixed(1) + '%';
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
      var topk = parseInt(document.getElementById('mat-topk').value) || rag.ragTopK;

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
