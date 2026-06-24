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
    overview:   { title: '总览',       render: renderOverview },
    assistant:  { title: 'AI助手',     render: renderAssistant },
    drawing:    { title: '图纸审核',   render: renderDrawing },
    selection:  { title: '快速选型',   render: renderSelection },
    material:   { title: '物料检索',   render: renderMaterial },
    design:     { title: '设计计算',   render: renderDesign },
    agent:      { title: 'Agent任务',  render: renderAgent },
    settings:   { title: '设置',       render: renderSettings }
  };

  var DEFAULT_PAGE = 'overview';

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
    hermesOnline: false,
    ragOnline: false,          // RAG 服务状态
    tasks: [],
    activeJob: null,
    settings: {
      executionMode: 'local',
      hermesUrl: 'http://localhost:5000',
      contextMode: 'full',
      deployDir: 'D:\\SWAgentAddin\\frontend\\property-workbench',
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

  function isDefaultCheckablePart(node) {
    if (!isSubmittableNode(node)) return false;
    if (node.isSuppressed) return false;
    // TODO: 轻化/封套/BOM 排除等待后端字段；当前仅排除 isSuppressed
    return true;
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
      if (checked) state.checkedNodeIds.add(node.id);
      else state.checkedNodeIds.delete(node.id);
    }
    refreshCheckedUi();
  }

  function refreshCheckedUi() {
    refreshDesignTree();
    updateOverviewSummary();
    updateOverviewBottomStats();
    renderActionList();
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
    return status === 'completed' || status === 'failed' || status === 'cancelled' || status === 'canceled';
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
      message: data.message || fallback.message || ''
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

  function handleJobSubmitResult(result) {
    var data = getResultData(result);
    if (!isCommandSuccess(result) || !(data.job_id || data.jobId || data.id)) {
      state.activeJob = normalizeJobData(data, state.activeJob);
      state.activeJob.status = 'failed';
      state.activeJob.current_stage = '受理失败';
      state.activeJob.message = (result && result.message) || (result && result.error && result.error.message) || '任务提交失败，请检查 Hermes 服务或 C# 返回。';
      clearJobPollTimer();
      renderJobStatusPanel();
      renderStatusbar();
      addAIMessage('system', state.activeJob.message);
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
    renderJobStatusPanel();
    renderStatusbar();
    if (isTerminalJobStatus(state.activeJob.status)) {
      clearJobPollTimer();
      addAIMessage('system', '任务状态：' + state.activeJob.status);
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
    var statusClass = job.status === 'failed' ? 'error' : (terminal ? 'done' : 'running');
    var submittedCount = job.payload && job.payload.components ? job.payload.components.length : 0;
    return '' +
      '<div class="job-panel ' + statusClass + '">' +
        '<div class="job-panel-head">' +
          '<div>' +
            '<div class="job-title">任务' + (job.accepted ? '已受理' : '提交中') + '</div>' +
            '<div class="job-subtitle">' + esc(job.source || 'Agent Job') + '</div>' +
          '</div>' +
          '<span class="badge badge-status ' + (statusClass === 'running' ? 'warning' : '') + '">' + esc(statusLabel(job.status)) + '</span>' +
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
      return {
        id: nodeId,
        name: node.DisplayName || node.displayName || node.Name || node.name || row.DisplayName || row.displayName || '(unnamed)',
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
        return {
          id: id,
          name: row.DisplayName || row.displayName || id,
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
        state.context = normalizeContext(context);
        if (state.context) {
          applyRuntimeConfig(state.context._runtimeConfig);
          state.selectedNode = state.context.tree;
          initDefaultCheckedNodeIds();
          state.expandedSet.clear();
          state.expandedSet.add(state.context.tree.id);
          if (state.context.tree.children) {
            state.context.tree.children.forEach(function (c) { state.expandedSet.add(c.id); });
          }
        }
        renderTopbar();
        renderStatusbar();
        if (state.currentPage === 'overview') navigatePage('overview');
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
        } else if (cmd === 'material.properties.review.submit' || cmd === 'agent.job.submit' || (!cmd && state.activeJob && state.activeJob.status === 'submitting' && (data.job_id || data.jobId || data.id))) {
          handleJobSubmitResult(result);
        } else if (cmd === 'agent.job.poll' || (!cmd && state.activeJob && state.activeJob.job_id && (data.job_id === state.activeJob.job_id || data.jobId === state.activeJob.job_id))) {
          handleJobPollResult(result);
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
          if (state.currentPage === 'overview') updateOverviewSummary();
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
          if (type === 'ai.material.search') {
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
    if (!topbar || topbar.querySelector('.window-controls')) return;
    var controls = document.createElement('div');
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
    container.classList.toggle('page-overview-active', pageId === 'overview');
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

    window.MechPilot.sendCommand('ai.assistant.chat', payload);

    setTimeout(function () {
      var reply = '收到：' + text + '\n当前页面：' + PAGES[state.currentPage].title;
      if (c) reply += '\n当前文件：' + c.fileName;
      if (sel) reply += '\n选中对象：' + sel.name;
      reply += '\n\n⏳ Agent 疯狂输出中，请稍后…';
      addAIMessage('ai', reply);
    }, 400);
  }

  // ══════════════════════════════════════════════════════════
  //  设计树渲染（总览专用）
  // ══════════════════════════════════════════════════════════
  function renderDesignTree(container) {
    var tree = state.context ? state.context.tree : null;
    if (!tree) {
      container.innerHTML = '<div class="tree-empty">等待数据注入…</div>';
      return;
    }

    var el = document.createElement('div');
    el.className = 'design-tree';
    el.id = 'design-tree';
    el.appendChild(buildTreeNode(tree, 0));
    container.appendChild(el);
  }

  function buildTreeNode(node, depth) {
    var div = document.createElement('div');
    div.className = 'tree-node';
    div.setAttribute('data-id', node.id);

    var hasChildren = node.children && node.children.length > 0;
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
    name.textContent = node.name;
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

    // 点击高亮（不影响勾选）
    row.addEventListener('click', function () {
      state.selectedNode = node;
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
  function renderOverview(container) {
    var total = state.context && state.context.tree ? countNodes(state.context.tree) - 1 : 0;
    var parts = state.context && state.context.tree ? countParts(state.context.tree) : 0;
    var assy = total - parts;

    container.innerHTML =
      '<div class="overview-page">' +
        '<div class="page-title">总览</div>' +
        '<div class="overview-body">' +
          '<div class="overview-layout">' +
            '<div class="overview-left">' +
              '<div class="panel-header">设计树</div>' +
              '<div class="design-tree-container" id="design-tree-container"></div>' +
            '</div>' +
            '<div class="overview-center">' +
              '<div class="panel-header">选中对象摘要</div>' +
              '<div class="summary-card" id="summary-card"></div>' +
            '</div>' +
            '<div class="overview-right">' +
              '<div class="panel-header">可执行动作</div>' +
              '<div class="overview-right-body">' +
                '<div class="action-list" id="action-list"></div>' +
                '<div class="job-status-inline" data-job-status-panel></div>' +
              '</div>' +
            '</div>' +
          '</div>' +
        '</div>' +
        '<footer class="overview-stats-bar" aria-label="总览统计">' +
          '<div class="stats-inline">' +
            '<div class="stat-item"><span class="stat-label">零部件总数</span><span class="stat-value" id="stat-total">' + total + '</span></div>' +
            '<div class="stat-item"><span class="stat-label">零件数</span><span class="stat-value" id="stat-parts">' + parts + '</span></div>' +
            '<div class="stat-item"><span class="stat-label">装配体数</span><span class="stat-value" id="stat-assy">' + assy + '</span></div>' +
            '<div class="stat-item"><span class="stat-label">属性完整度</span><span class="stat-value" id="stat-completeness">62%</span></div>' +
            '<div class="stat-item"><span class="stat-label">已勾选数量</span><span class="stat-value" id="stat-checked-parts">0</span></div>' +
          '</div>' +
        '</footer>' +
      '</div>';

    // 渲染设计树
    renderDesignTree(document.getElementById('design-tree-container'));

    // 渲染摘要
    updateOverviewSummary();
    updateOverviewBottomStats();

    // 渲染动作列表
    renderActionList();
    renderJobStatusPanel();
  }

  function updateOverviewBottomStats() {
    var checkedPartsEl = document.getElementById('stat-checked-parts');
    if (checkedPartsEl) checkedPartsEl.textContent = String(getCheckedPartCount());
  }

  function updateOverviewSummary() {
    var card = document.getElementById('summary-card');
    if (!card) return;

    var node = state.selectedNode;
    var batchHtml =
      '<div class="summary-batch">' +
        '<div class="kv"><span class="k">已勾选</span><span class="v"><b>' + getCheckedNodeCount() + '</b> 项</span></div>' +
        '<div class="kv"><span class="k">已勾选零件</span><span class="v"><b>' + getCheckedPartCount() + '</b> 个</span></div>' +
      '</div>';

    if (!node) {
      card.innerHTML = batchHtml + '<p class="hint">请在左侧设计树中点击一个节点查看详情。</p>';
      return;
    }

    var propsHtml = '';
    if (node.properties) {
      var keys = Object.keys(node.properties).slice(0, 6);
      keys.forEach(function (key) {
        var v = node.properties[key];
        var display = v && v.resolved ? v.resolved : (v && v.raw ? v.raw : '');
        if (display) {
          propsHtml += '<div class="kv"><span class="k">' + esc(key) + '</span><span class="v">' + esc(display) + '</span></div>';
        }
      });
    }

    card.innerHTML =
      batchHtml +
      '<div class="summary-section-label">当前高亮</div>' +
      '<div class="summary-header">' +
        '<span class="summary-icon">' + (node.type === 'assembly' ? ICONS.assembly : ICONS.part) + '</span>' +
        '<span class="summary-name">' + esc(node.name) + '</span>' +
      '</div>' +
      '<div class="summary-body">' +
        '<div class="kv"><span class="k">类型</span><span class="v">' + esc(node.docType || node.type) + '</span></div>' +
        '<div class="kv"><span class="k">数量</span><span class="v">' + node.quantity + '</span></div>' +
        '<div class="kv"><span class="k">文件路径</span><span class="v" title="' + esc(node.filePath) + '">' + esc(node.filePath || '(内置)') + '</span></div>' +
        '<div class="kv"><span class="k">文件大小</span><span class="v">' + esc(node.fileSize || '-') + '</span></div>' +
        (node.isSuppressed ? '<div class="kv"><span class="k">状态</span><span class="v warn">抑制</span></div>' : '') +
        (node.isLightweight ? '<div class="kv"><span class="k">状态</span><span class="v warn">轻化</span></div>' : '') +
      '</div>' +
      '<div class="summary-props">' +
        '<div class="props-title">关键属性</div>' +
        (propsHtml || '<p class="hint">暂无属性数据</p>') +
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
          page: 'overview'
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
      if (state.hermesOnline) {
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
            '<div class="kv"><span class="k">Hermes 在线</span><span class="v">' + (state.hermesOnline ? '是' : '否') + '</span></div>' +
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
