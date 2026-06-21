/**
 * MechPilot Agent驾驶舱 — Mock 数据
 * 提供 window.MECHPILOT_MOCK_CONTEXT
 * 覆盖：assembly root、subassembly、重复零件数量、多属性列、raw/resolved 值
 */
(function () {
  'use strict';

  // ── 属性定义（动态列） ──────────────────────────────────
  var propertyDefs = [
    { key: 'material',       label: '材质',     type: 'string' },
    { key: 'weight',         label: '重量(kg)',  type: 'number' },
    { key: 'surfaceFinish',  label: '表面处理',   type: 'string' },
    { key: 'supplier',       label: '供应商',    type: 'string' },
    { key: 'partNumber',     label: '零件号',    type: 'string' },
    { key: 'tolerance',      label: '公差等级',   type: 'string' },
    { key: 'notes',          label: '备注',      type: 'string' }
  ];

  // ── 树节点 ─────────────────────────────────────────────
  // id, name, type('assembly'|'part'), docType, filePath, fileSize,
  // quantity, children[], properties{ key: { raw, resolved } }
  var tree = {
    id: 'root',
    name: '阀体总成 V2.3',
    type: 'assembly',
    docType: '装配体',
    filePath: 'D:\\Projects\\ValveAssy\\阀体总成.SLDASM',
    fileSize: '12.4 MB',
    quantity: 1,
    properties: {
      material:      { raw: '',          resolved: '' },
      weight:        { raw: '',          resolved: '28.6' },
      surfaceFinish: { raw: '',          resolved: '' },
      supplier:      { raw: '',          resolved: '自制' },
      partNumber:    { raw: 'VA-001',    resolved: 'VA-001' },
      tolerance:     { raw: '',          resolved: '' },
      notes:         { raw: '总装图',     resolved: '总装图' }
    },
    children: [
      // ── 子装配体 1：阀芯组件 ──
      {
        id: 'sub-01',
        name: '阀芯组件',
        type: 'assembly',
        docType: '装配体',
        filePath: 'D:\\Projects\\ValveAssy\\阀芯组件.SLDASM',
        fileSize: '4.2 MB',
        quantity: 1,
        properties: {
          material:      { raw: '',          resolved: '' },
          weight:        { raw: '',          resolved: '5.2' },
          surfaceFinish: { raw: '',          resolved: '' },
          supplier:      { raw: '',          resolved: '自制' },
          partNumber:    { raw: 'VC-001',    resolved: 'VC-001' },
          tolerance:     { raw: '',          resolved: '' },
          notes:         { raw: '',          resolved: '' }
        },
        children: [
          {
            id: 'p-001',
            name: '阀芯',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\阀芯.SLDPRT',
            fileSize: '1.8 MB',
            quantity: 1,
            properties: {
              material:      { raw: 'SW-材料@阀芯.SLDPRT', resolved: '06Cr19Ni10 (304不锈钢)' },
              weight:        { raw: 'SW-质量@阀芯.SLDPRT', resolved: '2.1' },
              surfaceFinish: { raw: 'Ra1.6',               resolved: 'Ra1.6' },
              supplier:      { raw: '',                     resolved: '华锐精工' },
              partNumber:    { raw: 'VC-001-01',            resolved: 'VC-001-01' },
              tolerance:     { raw: 'IT7',                  resolved: 'IT7' },
              notes:         { raw: '',                     resolved: '' }
            },
            children: []
          },
          {
            id: 'p-002',
            name: '阀杆',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\阀杆.SLDPRT',
            fileSize: '920 KB',
            quantity: 1,
            properties: {
              material:      { raw: 'SW-材料@阀杆.SLDPRT',  resolved: '2Cr13' },
              weight:        { raw: 'SW-质量@阀杆.SLDPRT',  resolved: '0.8' },
              surfaceFinish: { raw: 'Ra0.8',                resolved: 'Ra0.8' },
              supplier:      { raw: '',                      resolved: '华锐精工' },
              partNumber:    { raw: 'VC-001-02',             resolved: 'VC-001-02' },
              tolerance:     { raw: 'IT6',                   resolved: 'IT6' },
              notes:         { raw: '',                      resolved: '' }
            },
            children: []
          },
          {
            id: 'p-003',
            name: '弹簧座',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\弹簧座.SLDPRT',
            fileSize: '540 KB',
            quantity: 2,
            properties: {
              material:      { raw: 'SW-材料@弹簧座.SLDPRT', resolved: 'Q235A' },
              weight:        { raw: 'SW-质量@弹簧座.SLDPRT', resolved: '0.35' },
              surfaceFinish: { raw: 'Ra3.2',                  resolved: 'Ra3.2' },
              supplier:      { raw: '',                        resolved: '金鼎冲压' },
              partNumber:    { raw: 'VC-001-03',               resolved: 'VC-001-03' },
              tolerance:     { raw: 'IT9',                     resolved: 'IT9' },
              notes:         { raw: '共2件',                   resolved: '共2件' }
            },
            children: []
          }
        ]
      },
      // ── 子装配体 2：密封组件 ──
      {
        id: 'sub-02',
        name: '密封组件',
        type: 'assembly',
        docType: '装配体',
        filePath: 'D:\\Projects\\ValveAssy\\密封组件.SLDASM',
        fileSize: '2.1 MB',
        quantity: 1,
        properties: {
          material:      { raw: '',          resolved: '' },
          weight:        { raw: '',          resolved: '1.4' },
          surfaceFinish: { raw: '',          resolved: '' },
          supplier:      { raw: '',          resolved: '自制' },
          partNumber:    { raw: 'SE-001',    resolved: 'SE-001' },
          tolerance:     { raw: '',          resolved: '' },
          notes:         { raw: '',          resolved: '' }
        },
        children: [
          {
            id: 'p-004',
            name: 'O型密封圈',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\O型密封圈.SLDPRT',
            fileSize: '120 KB',
            quantity: 3,
            properties: {
              material:      { raw: 'SW-材料@O型密封圈.SLDPRT', resolved: '氟橡胶 (FKM)' },
              weight:        { raw: 'SW-质量@O型密封圈.SLDPRT', resolved: '0.02' },
              surfaceFinish: { raw: '',                          resolved: '' },
              supplier:      { raw: '',                          resolved: '恩福密封' },
              partNumber:    { raw: 'SE-001-01',                 resolved: 'SE-001-01' },
              tolerance:     { raw: '±0.1',                      resolved: '±0.1' },
              notes:         { raw: '3件/套',                    resolved: '3件/套' }
            },
            children: []
          },
          {
            id: 'p-005',
            name: '密封垫片',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\密封垫片.SLDPRT',
            fileSize: '85 KB',
            quantity: 2,
            properties: {
              material:      { raw: 'SW-材料@密封垫片.SLDPRT', resolved: '石墨复合' },
              weight:        { raw: 'SW-质量@密封垫片.SLDPRT', resolved: '0.05' },
              surfaceFinish: { raw: '',                          resolved: '' },
              supplier:      { raw: '',                          resolved: '中密控股' },
              partNumber:    { raw: 'SE-001-02',                 resolved: 'SE-001-02' },
              tolerance:     { raw: '±0.05',                     resolved: '±0.05' },
              notes:         { raw: '',                          resolved: '' }
            },
            children: []
          },
          {
            id: 'p-006',
            name: '压环',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\压环.SLDPRT',
            fileSize: '210 KB',
            quantity: 1,
            properties: {
              material:      { raw: 'SW-材料@压环.SLDPRT',   resolved: '06Cr19Ni10 (304不锈钢)' },
              weight:        { raw: 'SW-质量@压环.SLDPRT',   resolved: '0.18' },
              surfaceFinish: { raw: 'Ra1.6',                 resolved: 'Ra1.6' },
              supplier:      { raw: '',                       resolved: '华锐精工' },
              partNumber:    { raw: 'SE-001-03',              resolved: 'SE-001-03' },
              tolerance:     { raw: 'IT8',                    resolved: 'IT8' },
              notes:         { raw: '',                       resolved: '' }
            },
            children: []
          }
        ]
      },
      // ── 子装配体 3：法兰连接 ──
      {
        id: 'sub-03',
        name: '法兰连接',
        type: 'assembly',
        docType: '装配体',
        filePath: 'D:\\Projects\\ValveAssy\\法兰连接.SLDASM',
        fileSize: '3.6 MB',
        quantity: 1,
        properties: {
          material:      { raw: '',          resolved: '' },
          weight:        { raw: '',          resolved: '8.7' },
          surfaceFinish: { raw: '',          resolved: '' },
          supplier:      { raw: '',          resolved: '自制' },
          partNumber:    { raw: 'FL-001',    resolved: 'FL-001' },
          tolerance:     { raw: '',          resolved: '' },
          notes:         { raw: '',          resolved: '' }
        },
        children: [
          {
            id: 'p-007',
            name: '上法兰',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\上法兰.SLDPRT',
            fileSize: '2.3 MB',
            quantity: 1,
            properties: {
              material:      { raw: 'SW-材料@上法兰.SLDPRT',  resolved: 'WCB (碳钢铸件)' },
              weight:        { raw: 'SW-质量@上法兰.SLDPRT',  resolved: '4.5' },
              surfaceFinish: { raw: 'Ra6.3',                   resolved: 'Ra6.3' },
              supplier:      { raw: '',                         resolved: '河北法兰' },
              partNumber:    { raw: 'FL-001-01',                resolved: 'FL-001-01' },
              tolerance:     { raw: 'IT10',                     resolved: 'IT10' },
              notes:         { raw: 'DN50 PN16',               resolved: 'DN50 PN16' }
            },
            children: []
          },
          {
            id: 'p-008',
            name: '下法兰',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\下法兰.SLDPRT',
            fileSize: '2.3 MB',
            quantity: 1,
            properties: {
              material:      { raw: 'SW-材料@下法兰.SLDPRT',  resolved: 'WCB (碳钢铸件)' },
              weight:        { raw: 'SW-质量@下法兰.SLDPRT',  resolved: '4.5' },
              surfaceFinish: { raw: 'Ra6.3',                   resolved: 'Ra6.3' },
              supplier:      { raw: '',                         resolved: '河北法兰' },
              partNumber:    { raw: 'FL-001-02',                resolved: 'FL-001-02' },
              tolerance:     { raw: 'IT10',                     resolved: 'IT10' },
              notes:         { raw: 'DN50 PN16',               resolved: 'DN50 PN16' }
            },
            children: []
          },
          {
            id: 'p-009',
            name: '双头螺柱',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\双头螺柱.SLDPRT',
            fileSize: '65 KB',
            quantity: 8,
            properties: {
              material:      { raw: 'SW-材料@双头螺柱.SLDPRT', resolved: '35CrMoA' },
              weight:        { raw: 'SW-质量@双头螺柱.SLDPRT', resolved: '0.12' },
              surfaceFinish: { raw: '发黑',                      resolved: '发黑' },
              supplier:      { raw: '',                          resolved: '永年紧固件' },
              partNumber:    { raw: 'FL-001-03',                 resolved: 'FL-001-03' },
              tolerance:     { raw: '6g',                        resolved: '6g' },
              notes:         { raw: 'M16×90, 8件',              resolved: 'M16×90, 8件' }
            },
            children: []
          },
          {
            id: 'p-010',
            name: '螺母',
            type: 'part',
            docType: '零件',
            filePath: 'D:\\Projects\\ValveAssy\\Parts\\螺母.SLDPRT',
            fileSize: '38 KB',
            quantity: 16,
            properties: {
              material:      { raw: 'SW-材料@螺母.SLDPRT',  resolved: '35CrMoA' },
              weight:        { raw: 'SW-质量@螺母.SLDPRT',  resolved: '0.04' },
              surfaceFinish: { raw: '发黑',                   resolved: '发黑' },
              supplier:      { raw: '',                       resolved: '永年紧固件' },
              partNumber:    { raw: 'FL-001-04',              resolved: 'FL-001-04' },
              tolerance:     { raw: '6H',                     resolved: '6H' },
              notes:         { raw: 'M16, 16件',              resolved: 'M16, 16件' }
            },
            children: []
          }
        ]
      },
      // ── 顶层散件 ──
      {
        id: 'p-011',
        name: '阀体',
        type: 'part',
        docType: '零件',
        filePath: 'D:\\Projects\\ValveAssy\\Parts\\阀体.SLDPRT',
        fileSize: '5.6 MB',
        quantity: 1,
        properties: {
          material:      { raw: 'SW-材料@阀体.SLDPRT',  resolved: 'CF8M (不锈钢铸件)' },
          weight:        { raw: 'SW-质量@阀体.SLDPRT',  resolved: '13.2' },
          surfaceFinish: { raw: 'Ra3.2',                 resolved: 'Ra3.2' },
          supplier:      { raw: '',                       resolved: '浙江超达' },
          partNumber:    { raw: 'VA-001-BODY',            resolved: 'VA-001-BODY' },
          tolerance:     { raw: 'IT9',                    resolved: 'IT9' },
          notes:         { raw: '铸造+精加工',            resolved: '铸造+精加工' }
        },
        children: []
      },
      {
        id: 'p-012',
        name: '填料压盖',
        type: 'part',
        docType: '零件',
        filePath: 'D:\\Projects\\ValveAssy\\Parts\\填料压盖.SLDPRT',
        fileSize: '180 KB',
        quantity: 1,
        properties: {
          material:      { raw: 'SW-材料@填料压盖.SLDPRT', resolved: '06Cr19Ni10 (304不锈钢)' },
          weight:        { raw: 'SW-质量@填料压盖.SLDPRT', resolved: '0.45' },
          surfaceFinish: { raw: 'Ra1.6',                    resolved: 'Ra1.6' },
          supplier:      { raw: '',                          resolved: '华锐精工' },
          partNumber:    { raw: 'VA-001-GL',                resolved: 'VA-001-GL' },
          tolerance:     { raw: 'IT8',                       resolved: 'IT8' },
          notes:         { raw: '',                          resolved: '' }
        },
        children: []
      }
    ]
  };

  // ── 顶层 context ──────────────────────────────────────
  window.MECHPILOT_MOCK_CONTEXT = {
    fileName: '阀体总成 V2.3.SLDASM',
    filePath: 'D:\\Projects\\ValveAssy\\阀体总成.SLDASM',
    mode: '模拟模式',
    status: '就绪',
    timestamp: new Date().toISOString(),
    propertyDefs: propertyDefs,
    tree: tree
  };
})();
