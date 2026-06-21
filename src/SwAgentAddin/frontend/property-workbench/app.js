/**
 * MechPilot Agent驾驶舱 — 主逻辑
 * 功能：树渲染、属性表、树表联动、搜索、筛选、raw/resolved 切换、WebView2 bridge
 */
(function () {
  'use strict';

  // ══════════════════════════════════════════════════════════
  //  SVG 图标（内联，不依赖外部文件）
  // ══════════════════════════════════════════════════════════
  var ICONS = {
    chevron: '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 4l4 4-4 4"/></svg>',
    assembly: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="2" width="12" height="12" rx="2" stroke="currentColor" stroke-width="1.4"/><path d="M5 6h6M5 8h4M5 10h5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
    part: '<svg viewBox="0 0 16 16" fill="none"><rect x="3" y="2" width="10" height="12" rx="1.5" stroke="currentColor" stroke-width="1.4"/><path d="M5.5 5h5M5.5 7.5h4" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
    search: '<svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><circle cx="6.5" cy="6.5" r="4.5"/><path d="M10 10l4 4"/></svg>',
    filter: '<svg viewBox="0 0 12 12" fill="currentColor"><path d="M1 2h10l-3.5 4.5V10L4.5 9V6.5z"/></svg>',
    logo: '<svg width="18" height="18" viewBox="0 0 20 20" fill="none"><rect x="1" y="1" width="18" height="18" rx="4" fill="#2b6cb0"/><path d="M5 7h10M5 10h7M5 13h9" stroke="#fff" stroke-width="1.8" stroke-linecap="round"/></svg>',
    empty: '<svg width="48" height="48" viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="8" y="6" width="32" height="36" rx="3"/><path d="M16 16h16M16 22h12M16 28h14"/></svg>',
    collapseAll: '<svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><path d="M4 6l4 4 4-4"/><path d="M4 3l4 4 4-4" opacity=".4"/></svg>',
    expandAll: '<svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><path d="M4 6l4 4 4-4"/><path d="M4 10l4-4 4 4" opacity=".4"/></svg>'
  };

  // ══════════════════════════════════════════════════════════
  //  状态
  // ══════════════════════════════════════════════════════════
  var state = {
    context: null,            // 当前 context（mock 或 WebView2 注入）
    flatNodes: [],            // 树平铺（有序）
    selectedId: null,         // 当前选中节点 id
    valueMode: 'resolved',    // 'raw' | 'resolved'
    searchQuery: '',
    columnFilters: {},        // { colKey: Set<value> }
    expandedSet: new Set(),   // 展开的节点 id
    tableRows: []             // 当前渲染的行
  };

  // ══════════════════════════════════════════════════════════
  //  初始化
  // ══════════════════════════════════════════════════════════
  function normalizeContext(context) {
    if (!context) return null;
    if (typeof context === 'string') {
      try {
        context = JSON.parse(context);
      } catch (e) {
        console.error('[MechPilot] Invalid context JSON:', e);
        return null;
      }
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

    // Extract Summary / Warnings from C# context
    var summary = context.Summary || context.summary || null;
    var warnings = context.Warnings || context.warnings || [];
    var schemaVersion = context.SchemaVersion || context.schemaVersion || '';

    return {
      fileName: doc.Title || doc.title || table.TargetLabel || table.targetLabel || '(none)',
      filePath: doc.FilePath || doc.filePath || '',
      mode: client.ExecutionMode || client.executionMode || context.mode || 'local',
      status: '真实数据',
      timestamp: context.TimestampUtc || context.timestampUtc || context.timestamp || new Date().toISOString(),
      propertyDefs: columns.map(function (name) {
        return { key: name, label: name, type: 'text' };
      }),
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
      // Agent L 增强字段 — 供状态栏/调试使用
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
      _schemaVersion: schemaVersion,
      _isMock: false  // 标记为非 mock 数据
    };
  }

  function init() {
    // 使用 mock 数据或已注入的数据
    state.context = normalizeContext(window.MECHPILOT_MOCK_CONTEXT) || null;

    // 注入 WebView2 bridge
    injectBridge();
    installWindowControls();

    // 渲染
    if (state.context) {
      renderAll();
    } else {
      renderEmpty();
    }
  }

  // ══════════════════════════════════════════════════════════
  //  WebView2 Bridge
  // ══════════════════════════════════════════════════════════
  function injectBridge() {
    window.MechPilot = {
      /**
       * WebView2 → 前端：注入新 context（替换 mock 数据）
       */
      receiveContext: function (context) {
        state.context = normalizeContext(context);
        state.selectedId = null;
        state.expandedSet.clear();
        state.columnFilters = {};
        state.searchQuery = '';
        renderAll();
      },
      /**
       * WebView2 → 前端：接收 Agent 任务结果
       */
      receiveResult: function (result) {
        var sb = document.getElementById('status-agent');
        if (sb) sb.textContent = result.message || JSON.stringify(result);
      },
      /**
       * 前端 → WebView2：发送命令
       */
      sendCommand: function (type, payload) {
        var envelope = JSON.stringify({ type: type, payload: payload, ts: Date.now() });
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage(envelope);
        } else {
          console.log('[MechPilot] sendCommand (mock):', type, payload);
        }
      }
    };
  }

  // ══════════════════════════════════════════════════════════
  //  树 → 平铺（DFS）
  // ══════════════════════════════════════════════════════════
  function installWindowControls() {
    var observer = new MutationObserver(function () {
      ensureWindowControls();
    });
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
    var topbar = document.getElementById('topbar') || document.querySelector('.topbar');
    if (!topbar || topbar.querySelector('.window-controls')) return;

    var controls = document.createElement('div');
    controls.className = 'window-controls';
    controls.setAttribute('aria-label', 'Window controls');
    controls.innerHTML =
      '<button class="window-btn" data-window-command="window_minimize" title="Minimize" aria-label="Minimize"><span></span></button>' +
      '<button class="window-btn" data-window-command="window_maximize" title="Maximize / Restore" aria-label="Maximize or restore"><span></span></button>' +
      '<button class="window-btn window-close" data-window-command="window_close" title="Close" aria-label="Close"><span></span></button>';
    topbar.appendChild(controls);

    controls.querySelectorAll('.window-btn').forEach(function (btn) {
      btn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        window.MechPilot.sendCommand(this.getAttribute('data-window-command'), {});
      });
    });
  }

  function flattenTree(node, depth, parentPath) {
    var list = [];
    list.push({ node: node, depth: depth, parentPath: parentPath });
    if (node.children && node.children.length) {
      for (var i = 0; i < node.children.length; i++) {
        list = list.concat(flattenTree(node.children[i], depth + 1, parentPath + '/' + node.id));
      }
    }
    return list;
  }

  // 收集所有节点 id
  function collectAllIds(node, acc) {
    acc = acc || [];
    acc.push(node.id);
    if (node.children) {
      for (var i = 0; i < node.children.length; i++) {
        collectAllIds(node.children[i], acc);
      }
    }
    return acc;
  }

  // ══════════════════════════════════════════════════════════
  //  全量渲染
  // ══════════════════════════════════════════════════════════
  function renderAll() {
    renderTopbar();
    renderToolbar();
    renderTree();
    renderTable();
    renderStatusbar();
    bindResize();
  }

  function renderEmpty() {
    var app = document.getElementById('app');
    app.innerHTML =
      '<div class="topbar">' +
        '<div class="topbar-brand">' + ICONS.logo + ' MechPilot Agent驾驶舱</div>' +
      '</div>' +
      '<div class="main" style="align-items:center;justify-content:center;">' +
        '<div class="table-empty">' + ICONS.empty + '<p>等待数据注入…</p></div>' +
      '</div>';
  }

  // ── 顶栏 ────────────────────────────────────────────
  function renderTopbar() {
    var c = state.context;
    var el = document.getElementById('topbar');
    el.innerHTML =
      '<div class="topbar-brand">' + ICONS.logo + ' MechPilot Agent驾驶舱</div>' +
      '<div class="topbar-divider"></div>' +
      '<div class="topbar-info">' +
        '<span><span class="label">文件：</span><span class="value" id="tb-file">' + esc(c.fileName) + '</span></span>' +
        '<span><span class="label">路径：</span><span class="value" id="tb-path" title="' + esc(c.filePath) + '">' + esc(c.filePath) + '</span></span>' +
      '</div>' +
      '<div class="topbar-right">' +
        '<span class="badge badge-mode" id="tb-mode">' + esc(c.mode) + '</span>' +
        '<span class="badge badge-status" id="tb-status">' + esc(c.status) + '</span>' +
      '</div>';
  }

  // ── 工具栏 ──────────────────────────────────────────
  function renderToolbar() {
    var el = document.getElementById('toolbar');
    el.innerHTML =
      '<div class="search-box">' +
        ICONS.search +
        '<input type="text" id="search-input" placeholder="搜索零部件名称、属性值…" value="' + esc(state.searchQuery) + '">' +
      '</div>' +
      '<div class="sep"></div>' +
      '<span class="toolbar-label">数值显示：</span>' +
      '<div class="toggle-group">' +
        '<button class="toggle-btn' + (state.valueMode === 'resolved' ? ' active' : '') + '" data-mode="resolved">解析值</button>' +
        '<button class="toggle-btn' + (state.valueMode === 'raw' ? ' active' : '') + '" data-mode="raw">原始值</button>' +
      '</div>' +
      '<div class="sep"></div>' +
      '<span class="count-badge" id="row-count"></span>' +
      '<div class="toolbar-right">' +
        '<button class="btn-icon" id="btn-collapse" title="全部折叠">' + ICONS.collapseAll + '</button>' +
        '<button class="btn-icon" id="btn-expand" title="全部展开">' + ICONS.expandAll + '</button>' +
      '</div>';

    // 事件绑定
    document.getElementById('search-input').addEventListener('input', debounce(onSearch, 200));
    el.querySelectorAll('.toggle-btn').forEach(function (btn) {
      btn.addEventListener('click', function () {
        state.valueMode = this.getAttribute('data-mode');
        renderToolbar();
        renderTable();
      });
    });
    document.getElementById('btn-collapse').addEventListener('click', function () {
      state.expandedSet.clear();
      renderTree();
      renderTable();
    });
    document.getElementById('btn-expand').addEventListener('click', function () {
      var all = collectAllIds(state.context.tree);
      all.forEach(function (id) { state.expandedSet.add(id); });
      renderTree();
      renderTable();
    });
  }

  // ── 搜索 ────────────────────────────────────────────
  function onSearch(e) {
    state.searchQuery = e.target.value.trim().toLowerCase();
    renderTable();
  }

  // ── 树渲染 ──────────────────────────────────────────
  function renderTree() {
    var el = document.getElementById('tree-scroll');
    el.innerHTML = '';
    if (!state.context || !state.context.tree) return;

    // 默认展开根节点
    if (state.expandedSet.size === 0) {
      state.expandedSet.add(state.context.tree.id);
      if (state.context.tree.children) {
        state.context.tree.children.forEach(function (c) { state.expandedSet.add(c.id); });
      }
    }

    el.appendChild(buildTreeNode(state.context.tree, 0));
  }

  function buildTreeNode(node, depth) {
    var div = document.createElement('div');
    div.className = 'tree-node';
    div.setAttribute('data-id', node.id);

    var hasChildren = node.children && node.children.length > 0;
    var isExpanded = state.expandedSet.has(node.id);

    // 行
    var row = document.createElement('div');
    row.className = 'tree-row' + (state.selectedId === node.id ? ' selected' : '');
    row.style.paddingLeft = (8 + depth * 16) + 'px';
    row.setAttribute('data-id', node.id);

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
        renderTree();
        renderTable();
      });
    }
    row.appendChild(toggle);

    // 图标
    var icon = document.createElement('span');
    icon.className = 'tree-icon';
    icon.innerHTML = node.type === 'assembly' ? ICONS.assembly : ICONS.part;
    icon.style.color = node.type === 'assembly' ? '#2b6cb0' : '#86909c';
    row.appendChild(icon);

    // 名称
    var name = document.createElement('span');
    name.className = 'tree-name';
    name.textContent = node.name;
    name.title = node.name;
    row.appendChild(name);

    // 抑制/轻化标记
    if (node.isSuppressed) {
      var badge = document.createElement('span');
      badge.className = 'tree-badge tree-badge-suppressed';
      badge.textContent = '抑制';
      row.appendChild(badge);
    }
    if (node.isLightweight) {
      var badge = document.createElement('span');
      badge.className = 'tree-badge tree-badge-lightweight';
      badge.textContent = '轻化';
      row.appendChild(badge);
    }
    // 数量
    if (node.quantity > 1) {
      var qty = document.createElement('span');
      qty.className = 'tree-qty';
      qty.textContent = '×' + node.quantity;
      row.appendChild(qty);
    }

    // 点击选中
    row.addEventListener('click', function () {
      selectNode(node.id);
    });

    div.appendChild(row);

    // 子节点
    if (hasChildren) {
      var childWrap = document.createElement('div');
      childWrap.className = 'tree-children' + (isExpanded ? '' : ' collapsed');
      for (var i = 0; i < node.children.length; i++) {
        childWrap.appendChild(buildTreeNode(node.children[i], depth + 1));
      }
      div.appendChild(childWrap);
    }

    return div;
  }

  // ── 选中节点 → 联动 ────────────────────────────────
  function selectNode(id) {
    state.selectedId = id;
    // 更新树高亮
    document.querySelectorAll('.tree-row').forEach(function (r) {
      r.classList.toggle('selected', r.getAttribute('data-id') === id);
    });
    // 更新表高亮
    document.querySelectorAll('.prop-table tbody tr').forEach(function (tr) {
      tr.classList.toggle('selected', tr.getAttribute('data-id') === id);
    });
    // 滚动到可见
    var target = document.querySelector('.prop-table tbody tr[data-id="' + id + '"]');
    if (target) {
      target.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
    // 通知 WebView2
    window.MechPilot.sendCommand('select', { id: id });
  }

  // ── 表格渲染 ────────────────────────────────────────
  function renderTable() {
    var ctx = state.context;
    if (!ctx) return;

    var defs = ctx.propertyDefs || [];
    var rows = [];
    collectRows(ctx.tree, rows);
    state.flatNodes = rows;

    // 过滤
    var filtered = filterRows(rows);

    // 更新计数
    var countEl = document.getElementById('row-count');
    if (countEl) countEl.textContent = filtered.length + ' / ' + rows.length + ' 项';

    // 构建表格 HTML
    var html = '<table class="prop-table"><thead><tr>';
    // 固有列
    html += '<th class="col-name"><div class="th-inner"><span class="th-label">零部件名称</span></div></th>';
    html += '<th class="col-qty"><div class="th-inner"><span class="th-label">数量</span></div></th>';
    html += '<th class="col-type"><div class="th-inner"><span class="th-label">文档类型</span>' +
            '<span class="th-filter' + (state.columnFilters['docType'] ? ' active' : '') + '" data-col="docType" title="筛选">' + ICONS.filter + '</span></div></th>';
    html += '<th class="col-path"><div class="th-inner"><span class="th-label">文件路径</span></div></th>';
    html += '<th class="col-size"><div class="th-inner"><span class="th-label">文件大小</span></div></th>';
    // 动态属性列
    for (var d = 0; d < defs.length; d++) {
      var def = defs[d];
      html += '<th class="col-custom"><div class="th-inner"><span class="th-label">' + esc(def.label) + '</span>' +
              '<span class="th-filter' + (state.columnFilters[def.key] ? ' active' : '') + '" data-col="' + def.key + '" title="筛选">' + ICONS.filter + '</span></div></th>';
    }
    html += '</tr></thead><tbody>';

    if (filtered.length === 0) {
      html += '<tr><td colspan="' + (5 + defs.length) + '" style="text-align:center;color:var(--text-dim);padding:40px 0;">无匹配数据</td></tr>';
    } else {
      for (var i = 0; i < filtered.length; i++) {
        var item = filtered[i];
        var n = item.node;
        var indent = item.depth * 16;
        html += '<tr data-id="' + n.id + '"' + (state.selectedId === n.id ? ' class="selected"' : '') + '>';
        html += '<td class="col-name"><span style="padding-left:' + indent + 'px">' + esc(n.name) + '</span></td>';
        html += '<td class="col-qty cell-number">' + n.quantity + '</td>';
        html += '<td class="col-type">' + esc(n.docType) + '</td>';
        html += '<td class="col-path" title="' + esc(n.filePath) + '">' + esc(n.filePath) + '</td>';
        html += '<td class="col-size">' + esc(n.fileSize) + '</td>';
        for (var d2 = 0; d2 < defs.length; d2++) {
          var k = defs[d2].key;
          var prop = n.properties && n.properties[k];
          var val = '';
          var cellClass = 'cell-resolved';
          if (prop) {
            if (state.valueMode === 'raw') {
              val = prop.raw || '';
              cellClass = 'cell-raw';
            } else {
              val = prop.resolved || '';
            }
          }
          if (defs[d2].type === 'number' && val) cellClass += ' cell-number';
          html += '<td class="col-custom ' + cellClass + '" title="' + esc(val) + '">' + esc(val) + '</td>';
        }
        html += '</tr>';
      }
    }
    html += '</tbody></table>';

    var scroll = document.getElementById('table-scroll');
    scroll.innerHTML = html;

    // 绑定行点击
    scroll.querySelectorAll('tbody tr[data-id]').forEach(function (tr) {
      tr.addEventListener('click', function () {
        var id = this.getAttribute('data-id');
        selectNode(id);
        // 树联动高亮
        highlightTreeNode(id);
      });
    });

    // 绑定筛选
    scroll.querySelectorAll('.th-filter').forEach(function (el) {
      el.addEventListener('click', function (e) {
        e.stopPropagation();
        showFilterPopup(this.getAttribute('data-col'), this);
      });
    });
  }

  // 收集所有行（DFS）
  function collectRows(node, list, depth) {
    depth = depth || 0;
    // 只收集可见节点（父节点展开的）
    list.push({ node: node, depth: depth });
    if (node.children && node.children.length && state.expandedSet.has(node.id)) {
      for (var i = 0; i < node.children.length; i++) {
        collectRows(node.children[i], list, depth + 1);
      }
    }
  }

  // 过滤
  function filterRows(rows) {
    return rows.filter(function (item) {
      var n = item.node;
      // 搜索
      if (state.searchQuery) {
        var haystack = (n.name + ' ' + n.docType + ' ' + n.filePath + ' ' + n.fileSize).toLowerCase();
        if (n.properties) {
          Object.keys(n.properties).forEach(function (k) {
            var p = n.properties[k];
            haystack += ' ' + (p.raw || '') + ' ' + (p.resolved || '');
          });
          haystack = haystack.toLowerCase();
        }
        if (haystack.indexOf(state.searchQuery) === -1) return false;
      }
      // 列筛选
      var keys = Object.keys(state.columnFilters);
      for (var i = 0; i < keys.length; i++) {
        var col = keys[i];
        var allowed = state.columnFilters[col];
        if (!allowed || allowed.size === 0) continue;
        var val = '';
        if (col === 'docType') {
          val = n.docType;
        } else if (n.properties && n.properties[col]) {
          val = state.valueMode === 'raw' ? n.properties[col].raw : n.properties[col].resolved;
        }
        if (!allowed.has(val)) return false;
      }
      return true;
    });
  }

  // ── 筛选弹层 ───────────────────────────────────────
  var currentFilterPopup = null;
  function showFilterPopup(colKey, anchor) {
    closeFilterPopup();

    // 收集该列所有值
    var allRows = [];
    collectRows(state.context.tree, allRows);
    var valueSet = new Set();
    allRows.forEach(function (item) {
      var n = item.node;
      var val = '';
      if (colKey === 'docType') {
        val = n.docType;
      } else if (n.properties && n.properties[colKey]) {
        val = state.valueMode === 'raw' ? n.properties[colKey].raw : n.properties[colKey].resolved;
      }
      if (val) valueSet.add(val);
    });

    var values = Array.from(valueSet).sort();
    var activeFilter = state.columnFilters[colKey];

    // 创建弹层
    var popup = document.createElement('div');
    popup.className = 'filter-popup show';

    var searchHtml = '<input type="text" placeholder="筛选…" data-filter-search>';
    var optHtml = '<div class="filter-options">';
    values.forEach(function (v) {
      var checked = !activeFilter || activeFilter.size === 0 || activeFilter.has(v);
      optHtml += '<label class="filter-opt"><input type="checkbox" value="' + esc(v) + '"' + (checked ? ' checked' : '') + '>' +
                 '<span>' + esc(v || '(空)') + '</span></label>';
    });
    optHtml += '</div>';
    var actHtml = '<div class="filter-actions">' +
                  '<button class="filter-clear">清除</button>' +
                  '<button class="primary filter-apply">确定</button></div>';

    popup.innerHTML = searchHtml + optHtml + actHtml;
    document.body.appendChild(popup);
    currentFilterPopup = { el: popup, col: colKey };

    // 定位
    var rect = anchor.getBoundingClientRect();
    popup.style.left = rect.left + 'px';
    popup.style.top = (rect.bottom + 4) + 'px';

    // 搜索过滤选项
    popup.querySelector('[data-filter-search]').addEventListener('input', function () {
      var q = this.value.toLowerCase();
      popup.querySelectorAll('.filter-opt').forEach(function (opt) {
        var text = opt.textContent.toLowerCase();
        opt.style.display = text.indexOf(q) !== -1 ? '' : 'none';
      });
    });

    // 确定
    popup.querySelector('.filter-apply').addEventListener('click', function () {
      var selected = new Set();
      popup.querySelectorAll('.filter-opt input:checked').forEach(function (cb) {
        selected.add(cb.value);
      });
      if (selected.size === values.length || selected.size === 0) {
        delete state.columnFilters[colKey];
      } else {
        state.columnFilters[colKey] = selected;
      }
      closeFilterPopup();
      renderTable();
    });

    // 清除
    popup.querySelector('.filter-clear').addEventListener('click', function () {
      delete state.columnFilters[colKey];
      closeFilterPopup();
      renderTable();
    });

    // 点外部关闭
    setTimeout(function () {
      document.addEventListener('click', onFilterOutsideClick);
    }, 10);
  }

  function closeFilterPopup() {
    if (currentFilterPopup) {
      currentFilterPopup.el.remove();
      currentFilterPopup = null;
      document.removeEventListener('click', onFilterOutsideClick);
    }
  }

  function onFilterOutsideClick(e) {
    if (currentFilterPopup && !currentFilterPopup.el.contains(e.target)) {
      closeFilterPopup();
    }
  }

  // ── 树节点高亮（从表格点击触发） ──────────────────
  function highlightTreeNode(id) {
    // 移除旧高亮
    document.querySelectorAll('.tree-row.highlighted').forEach(function (r) {
      r.classList.remove('highlighted');
    });
    var target = document.querySelector('.tree-row[data-id="' + id + '"]');
    if (target) {
      target.classList.add('highlighted');
      target.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  }

  // ── 状态栏 ──────────────────────────────────────────
  function renderStatusbar() {
    var el = document.getElementById('statusbar');
    var ctx = state.context;
    var summary = ctx._summary || {};
    var warnings = ctx._warnings || [];

    var totalParts = summary.partCount != null ? summary.partCount : 0;
    var totalAsm = summary.subAssemblyCount != null ? summary.subAssemblyCount : 0;
    var totalRows = summary.uniqueDocCount != null ? summary.uniqueDocCount : 0;
    var suppressedCount = summary.suppressedCount || 0;
    var lightweightCount = summary.lightweightCount || 0;
    var warningCount = warnings.length;
    var schemaVersion = ctx._schemaVersion || '';

    if (!summary.partCount) { countTypes(ctx.tree, function (t) { if (t === 'part') totalParts++; else totalAsm++; }); }

    var html = '';
    html += '<div class="statusbar-row">';
    html += '<span class="status-item"><span class="dot ' + (ctx._isMock ? 'dot-mock' : 'dot-real') + '"></span>' +
            esc(ctx.status || 'mock') + '</span>';
    html += '<span class="sep"></span>';
    html += '<span class="status-item" id="status-agent">就绪 — 等待 Agent 指令</span>';
    html += '<span class="sep"></span>';
    html += '<span class="status-item">模式：' + (state.valueMode === 'raw' ? '原始值' : '解析值') + '</span>';
    if (schemaVersion) {
      html += '<span class="sep"></span>';
      html += '<span class="status-item status-dim">' + esc(schemaVersion) + '</span>';
    }
    html += '</div>';

    html += '<div class="statusbar-row">';
    html += '<span class="status-item">零部件：<b>' + totalParts + '</b></span>';
    html += '<span class="status-item">装配体：<b>' + totalAsm + '</b></span>';
    if (totalRows) { html += '<span class="status-item">去重：<b>' + totalRows + '</b></span>'; }
    if (suppressedCount) { html += '<span class="status-item status-warn">抑制：' + suppressedCount + '</span>'; }
    if (lightweightCount) { html += '<span class="status-item status-warn">轻化：' + lightweightCount + '</span>'; }
    if (warningCount) {
      html += '<span class="status-item status-warn" id="warnings-toggle" title="点击查看详情" style="cursor:pointer">' +
              '\u26A0 警告：' + warningCount + '</span>';
    }
    html += '</div>';

    el.innerHTML = html;

    if (warningCount) {
      document.getElementById('warnings-toggle').addEventListener('click', function () {
        if (state.warningsVisible) { closeWarningsPopup(); return; }
        showWarningsPopup(warnings);
        state.warningsVisible = true;
      });
    }
  }

  function closeWarningsPopup() {
    var popup = document.getElementById('warnings-popup');
    if (popup) popup.remove();
    state.warningsVisible = false;
  }

  function showWarningsPopup(warnings) {
    closeFilterPopup();

    var html = '<div class="warnings-popup-header">采集警告 (' + warnings.length + ')</div>';
    html += '<div class="warnings-popup-body">';
    warnings.forEach(function (w) {
      var level = (w.Level || '').toLowerCase();
      var cls = level === 'error' || level === 'fatal' ? 'warn-error' : (level === 'warning' ? 'warn-warning' : 'warn-info');
      html += '<div class="warn-item ' + cls + '">';
      html += '<span class="warn-level">[' + esc(w.Level || '?') + ']</span>';
      html += '<span class="warn-target">' + esc(w.Target || '') + '</span>';
      html += '<span class="warn-msg">' + esc(w.Message || '') + '</span>';
      html += '</div>';
    });
    html += '</div>';
    html += '<div class="warnings-popup-footer"><button class="primary">关闭</button></div>';

    var popup = document.createElement('div');
    popup.id = 'warnings-popup';
    popup.className = 'warnings-popup';
    popup.innerHTML = html;
    document.body.appendChild(popup);

    popup.querySelector('button').addEventListener('click', function () { closeWarningsPopup(); });
  }

  function countTypes(node, fn) {
    fn(node.type);
    if (node.children) {
      for (var i = 0; i < node.children.length; i++) {
        countTypes(node.children[i], fn);
      }
    }
  }

  // ── 面板拖拽调整宽度 ──────────────────────────────
  function bindResize() {
    var handle = document.getElementById('resize-handle');
    var panel = document.getElementById('tree-panel');
    if (!handle || !panel) return;

    var startX, startW;
    handle.addEventListener('mousedown', function (e) {
      e.preventDefault();
      startX = e.clientX;
      startW = panel.offsetWidth;
      handle.classList.add('active');
      document.addEventListener('mousemove', onMove);
      document.addEventListener('mouseup', onUp);
    });

    function onMove(e) {
      var newW = startW + (e.clientX - startX);
      newW = Math.max(200, Math.min(420, newW));
      panel.style.width = newW + 'px';
    }
    function onUp() {
      handle.classList.remove('active');
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    }
  }

  // ══════════════════════════════════════════════════════════
  //  工具函数
  // ══════════════════════════════════════════════════════════
  function esc(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function debounce(fn, ms) {
    var t;
    return function () {
      var ctx = this, args = arguments;
      clearTimeout(t);
      t = setTimeout(function () { fn.apply(ctx, args); }, ms);
    };
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
