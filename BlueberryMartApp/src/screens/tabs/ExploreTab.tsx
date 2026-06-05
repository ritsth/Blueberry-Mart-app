import React, { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Dimensions,
  Modal,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { BarChart, LineChart, PieChart } from 'react-native-chart-kit';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import {
  Catalog,
  MeasureSpec,
  QueryResult,
  QuerySpec,
  SavedReport,
  createReport,
  deleteReport,
  getCatalog,
  listReports,
  runQuery,
  updateReport,
} from '../../services/analyticsService';

const SCREEN_WIDTH = Dimensions.get('window').width;
const CHART_WIDTH = SCREEN_WIDTH - 48;

const chartConfig = {
  backgroundGradientFrom: '#ffffff',
  backgroundGradientTo: '#ffffff',
  decimalPlaces: 0,
  color: (opacity = 1) => `rgba(20, 83, 45, ${opacity})`,
  labelColor: (opacity = 1) => `rgba(107, 114, 128, ${opacity})`,
  propsForDots: { r: '3', strokeWidth: '2', stroke: '#16a34a' },
  barPercentage: 0.6,
};

const PIE_COLORS = ['#16a34a', '#0284c7', '#d97706', '#7c3aed', '#dc2626', '#0d9488', '#db2777', '#65a30d'];
const YEARS = ['2023', '2024', '2025', '2026', 'All'];
const CHART_TYPES: ChartType[] = ['bar', 'line', 'pie', 'table'];

type ChartType = 'bar' | 'line' | 'pie' | 'table';

// Build a query spec from the current builder selections.
function buildSpec(
  measures: MeasureSpec[],
  dims: string[],
  year: string,
  completedOnly: boolean,
  chartType: ChartType,
): QuerySpec {
  const filters = [];
  if (year === 'All') filters.push({ field: 'order_date', op: 'gte', values: ['2023-01-01'] });
  else filters.push({ field: 'year', op: 'eq', values: [year] });
  if (completedOnly) filters.push({ field: 'payment_status', op: 'eq', values: ['completed'] });

  const orderBy = dims.length > 0 && measures.length > 0
    ? [{ field: `${measures[0].field}_${measures[0].agg}`, dir: 'desc' }]
    : undefined;

  return { measures, dimensions: dims, filters, orderBy, limit: 200, chartType };
}

// --- formatting helpers -------------------------------------------------------
function fmtNum(v: any): string {
  if (typeof v !== 'number') return String(v);
  return v.toLocaleString('en-NP', { maximumFractionDigits: 2 });
}
function fmtCell(v: any): string {
  if (v === null || v === undefined) return '—';
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  if (typeof v === 'number') return fmtNum(v);
  return String(v);
}
function shortLabel(v: any): string {
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  let s = String(v).replace('Blueberry Mart ', '');
  return s.length > 10 ? s.slice(0, 9) + '…' : s;
}
function toNum(v: any): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

export default function ExploreTab() {
  const insets = useSafeAreaInsets();
  const [view, setView] = useState<'build' | 'saved'>('build');

  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);

  // builder state — sensible default that produces a good first chart
  const [measures, setMeasures] = useState<MeasureSpec[]>([{ field: 'line_revenue', agg: 'sum' }]);
  const [dims, setDims] = useState<string[]>(['category']);
  const [year, setYear] = useState('All'); // 'All' so the first chart lands on data regardless of year
  const [completedOnly, setCompletedOnly] = useState(true);
  const [chartType, setChartType] = useState<ChartType>('bar');

  const [result, setResult] = useState<QueryResult | null>(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);

  // saving
  const [loadedReportId, setLoadedReportId] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [saveName, setSaveName] = useState('');
  const [saving, setSaving] = useState(false);

  // saved list
  const [reports, setReports] = useState<SavedReport[]>([]);
  const [reportsLoading, setReportsLoading] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const c = await getCatalog();
        setCatalog(c);
        if (c.enabled) runSpec(buildSpec(measures, dims, year, completedOnly, chartType));
      } catch (e: any) {
        setCatalogError(e?.message ?? 'Failed to load.');
      }
    })();
  }, []);

  async function runSpec(spec: QuerySpec) {
    setRunning(true);
    setRunError(null);
    try {
      const res = await runQuery(spec);
      if (res.enabled === false) {
        setRunError('Analytics warehouse is not configured.');
        setResult(null);
      } else {
        setResult(res);
      }
    } catch (e: any) {
      setRunError(e?.message ?? 'Query failed.');
      setResult(null);
    } finally {
      setRunning(false);
    }
  }

  function run() {
    if (measures.length === 0) {
      setRunError('Pick at least one measure.');
      return;
    }
    runSpec(buildSpec(measures, dims, year, completedOnly, chartType));
  }

  function toggleMeasure(field: string, agg: string) {
    setMeasures(prev => {
      const exists = prev.some(x => x.field === field && x.agg === agg);
      if (exists) return prev.filter(x => !(x.field === field && x.agg === agg));
      if (prev.length >= 6) return prev;
      return [...prev, { field, agg }];
    });
  }

  function toggleDim(id: string) {
    setDims(prev => {
      if (prev.includes(id)) return prev.filter(d => d !== id);
      if (prev.length >= 3) return prev;
      return [...prev, id];
    });
  }

  async function doSave() {
    if (!saveName.trim()) return;
    setSaving(true);
    try {
      const spec = buildSpec(measures, dims, year, completedOnly, chartType);
      if (loadedReportId) {
        await updateReport(loadedReportId, saveName.trim(), spec);
      } else {
        const r = await createReport(saveName.trim(), spec);
        setLoadedReportId(r.id);
      }
      setSaveOpen(false);
    } catch (e: any) {
      Alert.alert('Save failed', e?.message ?? 'Could not save.');
    } finally {
      setSaving(false);
    }
  }

  async function openSaved() {
    setView('saved');
    setReportsLoading(true);
    try {
      setReports(await listReports());
    } catch {
      // surfaced via empty state
    } finally {
      setReportsLoading(false);
    }
  }

  function loadReport(r: SavedReport) {
    const cfg = r.config;
    setMeasures(cfg.measures ?? []);
    setDims(cfg.dimensions ?? []);
    const yf = cfg.filters?.find(f => f.field === 'year');
    const df = cfg.filters?.find(f => f.field === 'order_date');
    setYear(yf ? yf.values[0] : df ? 'All' : '2025');
    setCompletedOnly(!!cfg.filters?.some(f => f.field === 'payment_status'));
    setChartType((cfg.chartType as ChartType) ?? 'table');
    setLoadedReportId(r.id);
    setSaveName(r.name);
    setView('build');
    runSpec(cfg);
  }

  function confirmDelete(r: SavedReport) {
    Alert.alert('Delete report', `Delete "${r.name}"?`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: async () => {
          try {
            await deleteReport(r.id);
            setReports(prev => prev.filter(x => x.id !== r.id));
            if (loadedReportId === r.id) setLoadedReportId(null);
          } catch (e: any) {
            Alert.alert('Delete failed', e?.message ?? '');
          }
        },
      },
    ]);
  }

  // --- render -----------------------------------------------------------------
  if (catalogError) {
    return <View style={styles.centered}><Text style={styles.errorText}>{catalogError}</Text></View>;
  }
  if (!catalog) {
    return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  }
  if (!catalog.enabled) {
    return (
      <View style={[styles.centered, { paddingHorizontal: 32 }]}>
        <Ionicons name="cloud-offline-outline" size={40} color="#9ca3af" />
        <Text style={styles.disabledTitle}>Analytics warehouse not configured</Text>
        <Text style={styles.disabledNote}>
          The Explore feature needs the BigQuery warehouse. It's available in your dev
          environment.
        </Text>
      </View>
    );
  }

  return (
    <View style={styles.wrapper}>
      <View style={[styles.topBar, { paddingTop: insets.top + 12 }]}>
        <TouchableOpacity style={styles.topTabBtn} onPress={() => setView('build')} activeOpacity={0.7}>
          <Text style={[styles.topTabText, view === 'build' && styles.topTabTextActive]}>🧪  Build</Text>
          {view === 'build' && <View style={styles.topTabUnderline} />}
        </TouchableOpacity>
        <TouchableOpacity style={styles.topTabBtn} onPress={openSaved} activeOpacity={0.7}>
          <Text style={[styles.topTabText, view === 'saved' && styles.topTabTextActive]}>⭐  Saved</Text>
          {view === 'saved' && <View style={styles.topTabUnderline} />}
        </TouchableOpacity>
      </View>

      {view === 'saved' ? (
        <SavedList
          reports={reports}
          loading={reportsLoading}
          onOpen={loadReport}
          onDelete={confirmDelete}
        />
      ) : (
        <ScrollView style={styles.scroll} contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
          {/* MEASURES */}
          <Text style={styles.sectionTitle}>Measures</Text>
          <Text style={styles.hint}>What to count — tap an aggregation.</Text>
          {catalog.measures.map(m => (
            <View key={m.id} style={styles.fieldRow}>
              <Text style={styles.fieldLabel}>{m.label}</Text>
              <View style={styles.chipWrap}>
                {m.aggs.map(agg => {
                  const on = measures.some(x => x.field === m.id && x.agg === agg);
                  return (
                    <TouchableOpacity
                      key={agg}
                      style={[styles.chip, on && styles.chipOn]}
                      onPress={() => toggleMeasure(m.id, agg)}
                    >
                      <Text style={[styles.chipText, on && styles.chipTextOn]}>{agg.replace('_', ' ')}</Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </View>
          ))}

          {/* DIMENSIONS */}
          <Text style={[styles.sectionTitle, { marginTop: 18 }]}>Group by</Text>
          <Text style={styles.hint}>Up to 3 dimensions ({dims.length}/3).</Text>
          <View style={styles.chipWrap}>
            {catalog.dimensions.map(d => {
              const on = dims.includes(d.id);
              return (
                <TouchableOpacity
                  key={d.id}
                  style={[styles.chip, on && styles.chipOn]}
                  onPress={() => toggleDim(d.id)}
                >
                  <Text style={[styles.chipText, on && styles.chipTextOn]}>{d.label}</Text>
                </TouchableOpacity>
              );
            })}
          </View>

          {/* FILTERS */}
          <Text style={[styles.sectionTitle, { marginTop: 18 }]}>Time range</Text>
          <View style={styles.chipWrap}>
            {YEARS.map(y => (
              <TouchableOpacity
                key={y}
                style={[styles.chip, year === y && styles.chipOn]}
                onPress={() => setYear(y)}
              >
                <Text style={[styles.chipText, year === y && styles.chipTextOn]}>{y}</Text>
              </TouchableOpacity>
            ))}
          </View>
          <TouchableOpacity style={styles.toggleRow} onPress={() => setCompletedOnly(v => !v)} activeOpacity={0.7}>
            <Ionicons
              name={completedOnly ? 'checkbox' : 'square-outline'}
              size={20}
              color={completedOnly ? '#16a34a' : '#9ca3af'}
            />
            <Text style={styles.toggleText}>Completed orders only</Text>
          </TouchableOpacity>

          {/* CHART TYPE */}
          <Text style={[styles.sectionTitle, { marginTop: 18 }]}>Chart</Text>
          <View style={styles.chipWrap}>
            {CHART_TYPES.map(ct => (
              <TouchableOpacity
                key={ct}
                style={[styles.chip, chartType === ct && styles.chipOn]}
                onPress={() => setChartType(ct)}
              >
                <Text style={[styles.chipText, chartType === ct && styles.chipTextOn]}>{ct}</Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* ACTIONS */}
          <View style={styles.actions}>
            <TouchableOpacity style={styles.runBtn} onPress={run} disabled={running} activeOpacity={0.85}>
              {running
                ? <ActivityIndicator color="#fff" />
                : <Text style={styles.runBtnText}>Run</Text>}
            </TouchableOpacity>
            <TouchableOpacity
              style={styles.saveBtn}
              onPress={() => { setSaveName(saveName || 'My report'); setSaveOpen(true); }}
              activeOpacity={0.85}
            >
              <Ionicons name="bookmark-outline" size={16} color="#14532d" />
              <Text style={styles.saveBtnText}>{loadedReportId ? 'Update' : 'Save'}</Text>
            </TouchableOpacity>
          </View>

          {/* RESULT */}
          {runError && <Text style={styles.errorText}>{runError}</Text>}
          {result && <ResultView result={result} chartType={chartType} />}

          <View style={{ height: 32 }} />
        </ScrollView>
      )}

      {/* SAVE MODAL */}
      <Modal visible={saveOpen} transparent animationType="fade" onRequestClose={() => setSaveOpen(false)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>{loadedReportId ? 'Update report' : 'Save report'}</Text>
            <TextInput
              style={styles.input}
              value={saveName}
              onChangeText={setSaveName}
              placeholder="Report name"
              placeholderTextColor="#9ca3af"
              autoFocus
            />
            <View style={styles.modalActions}>
              <TouchableOpacity onPress={() => setSaveOpen(false)} style={styles.modalCancel}>
                <Text style={styles.modalCancelText}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity onPress={doSave} style={styles.modalSave} disabled={saving}>
                {saving ? <ActivityIndicator color="#fff" /> : <Text style={styles.modalSaveText}>Save</Text>}
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

// --- result rendering ---------------------------------------------------------
function ResultView({ result, chartType }: { result: QueryResult; chartType: ChartType }) {
  const dimensionCols = result.columns.filter(c => c.role === 'dimension');
  const measureCols = result.columns.filter(c => c.role === 'measure');
  const rows = result.rows;

  if (rows.length === 0) {
    return <Text style={styles.emptyNote}>No rows for this selection.</Text>;
  }

  const canChart = chartType !== 'table' && dimensionCols.length >= 1 && measureCols.length >= 1;

  return (
    <View style={{ marginTop: 16 }}>
      {canChart && (
        <ChartBlock
          rows={rows}
          dimKey={dimensionCols[0].key}
          measureKey={measureCols[0].key}
          measureLabel={measureCols[0].label}
          chartType={chartType}
        />
      )}
      <DataTable columns={result.columns} rows={rows} />
    </View>
  );
}

function ChartBlock({
  rows, dimKey, measureKey, measureLabel, chartType,
}: { rows: Record<string, any>[]; dimKey: string; measureKey: string; measureLabel: string; chartType: ChartType }) {
  const cap = chartType === 'pie' ? 8 : 12;
  const slice = rows.slice(0, cap);
  const labels = slice.map(r => shortLabel(r[dimKey]));
  const data = slice.map(r => toNum(r[measureKey]));

  return (
    <View style={styles.chartCard}>
      {chartType === 'bar' && (
        <BarChart
          data={{ labels, datasets: [{ data }] }}
          width={CHART_WIDTH} height={230} chartConfig={chartConfig}
          fromZero showValuesOnTopOfBars yAxisLabel="" yAxisSuffix="" style={styles.chart}
        />
      )}
      {chartType === 'line' && (
        <LineChart
          data={{ labels, datasets: [{ data }] }}
          width={CHART_WIDTH} height={230} chartConfig={chartConfig}
          bezier fromZero yAxisLabel="" yAxisSuffix="" style={styles.chart}
        />
      )}
      {chartType === 'pie' && (
        <PieChart
          data={slice.map((r, i) => ({
            name: shortLabel(r[dimKey]),
            population: Math.max(0, toNum(r[measureKey])),
            color: PIE_COLORS[i % PIE_COLORS.length],
            legendFontColor: '#374151',
            legendFontSize: 12,
          }))}
          width={CHART_WIDTH} height={200} chartConfig={chartConfig}
          accessor="population" backgroundColor="transparent" paddingLeft="12"
        />
      )}
      {rows.length > cap && <Text style={styles.truncNote}>Showing top {cap} of {rows.length}</Text>}
      <Text style={styles.chartCaption}>{measureLabel}</Text>
    </View>
  );
}

function DataTable({ columns, rows }: { columns: QueryResult['columns']; rows: Record<string, any>[] }) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.tableScroll}>
      <View>
        <View style={[styles.tr, styles.trHead]}>
          {columns.map(c => (
            <Text key={c.key} style={[styles.th, c.role === 'measure' && styles.thNum]} numberOfLines={1}>
              {c.label}
            </Text>
          ))}
        </View>
        {rows.slice(0, 100).map((r, i) => (
          <View key={i} style={[styles.tr, i % 2 === 1 && styles.trAlt]}>
            {columns.map(c => (
              <Text key={c.key} style={[styles.td, c.role === 'measure' && styles.tdNum]} numberOfLines={1}>
                {fmtCell(r[c.key])}
              </Text>
            ))}
          </View>
        ))}
      </View>
    </ScrollView>
  );
}

function SavedList({
  reports, loading, onOpen, onDelete,
}: { reports: SavedReport[]; loading: boolean; onOpen: (r: SavedReport) => void; onDelete: (r: SavedReport) => void }) {
  if (loading) return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  if (reports.length === 0) {
    return (
      <View style={[styles.centered, { paddingHorizontal: 32 }]}>
        <Ionicons name="bookmark-outline" size={36} color="#d1d5db" />
        <Text style={styles.disabledNote}>No saved reports yet. Build a chart and tap Save.</Text>
      </View>
    );
  }
  return (
    <ScrollView style={styles.scroll} contentContainerStyle={styles.content}>
      {reports.map(r => (
        <TouchableOpacity key={r.id} style={styles.reportRow} onPress={() => onOpen(r)} activeOpacity={0.7}>
          <View style={{ flex: 1 }}>
            <Text style={styles.reportName}>{r.name}</Text>
            <Text style={styles.reportMeta}>
              {(r.config.chartType ?? 'table')} · {(r.config.measures ?? []).length} measure(s) · {(r.config.dimensions ?? []).length} group(s)
            </Text>
          </View>
          <TouchableOpacity onPress={() => onDelete(r)} hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}>
            <Ionicons name="trash-outline" size={18} color="#dc2626" />
          </TouchableOpacity>
        </TouchableOpacity>
      ))}
      <View style={{ height: 32 }} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  wrapper: { flex: 1, backgroundColor: '#f9fafb' },
  topBar: {
    flexDirection: 'row', paddingHorizontal: 24, backgroundColor: '#ffffff',
    borderBottomWidth: 1, borderBottomColor: '#f3f4f6',
  },
  topTabBtn: { marginRight: 28, paddingBottom: 12, alignItems: 'center' },
  topTabText: { fontSize: 16, fontWeight: '600', color: '#9ca3af' },
  topTabTextActive: { color: '#111827' },
  topTabUnderline: { position: 'absolute', bottom: 0, left: 0, right: 0, height: 2.5, backgroundColor: '#111827', borderRadius: 2 },

  scroll: { flex: 1 },
  content: { paddingHorizontal: 24, paddingTop: 18, paddingBottom: 32 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  errorText: { color: '#dc2626', fontSize: 13, marginTop: 12 },

  sectionTitle: { fontSize: 15, fontWeight: '700', color: '#111827', marginBottom: 2 },
  hint: { fontSize: 12, color: '#9ca3af', marginBottom: 8 },

  fieldRow: { marginBottom: 10 },
  fieldLabel: { fontSize: 13, fontWeight: '600', color: '#374151', marginBottom: 4 },
  chipWrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  chip: {
    paddingVertical: 6, paddingHorizontal: 12, borderRadius: 16,
    backgroundColor: '#ffffff', borderWidth: 1, borderColor: '#e5e7eb',
  },
  chipOn: { backgroundColor: '#14532d', borderColor: '#14532d' },
  chipText: { fontSize: 12.5, color: '#374151', fontWeight: '600' },
  chipTextOn: { color: '#ffffff' },

  toggleRow: { flexDirection: 'row', alignItems: 'center', marginTop: 12, gap: 8 },
  toggleText: { fontSize: 13.5, color: '#374151', fontWeight: '500' },

  actions: { flexDirection: 'row', gap: 12, marginTop: 22 },
  runBtn: {
    flex: 1, backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14,
    alignItems: 'center', justifyContent: 'center',
  },
  runBtnText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
  saveBtn: {
    flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 18,
    borderRadius: 12, borderWidth: 1.5, borderColor: '#14532d', backgroundColor: '#f0fdf4',
  },
  saveBtnText: { color: '#14532d', fontSize: 14, fontWeight: '700' },

  // chart
  chartCard: {
    backgroundColor: '#ffffff', borderRadius: 14, paddingVertical: 12,
    marginBottom: 14, alignItems: 'center', overflow: 'hidden',
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 6, elevation: 1,
  },
  chart: { borderRadius: 12 },
  chartCaption: { fontSize: 12, color: '#6b7280', fontWeight: '600', marginTop: 2 },
  truncNote: { fontSize: 11, color: '#9ca3af', marginTop: 2 },

  // table
  tableScroll: { marginTop: 4 },
  tr: { flexDirection: 'row' },
  trHead: { borderBottomWidth: 1.5, borderBottomColor: '#e5e7eb', paddingBottom: 6, marginBottom: 4 },
  trAlt: { backgroundColor: '#f9fafb' },
  th: { minWidth: 96, paddingHorizontal: 8, fontSize: 12, fontWeight: '700', color: '#374151' },
  thNum: { textAlign: 'right' },
  td: { minWidth: 96, paddingHorizontal: 8, paddingVertical: 5, fontSize: 12.5, color: '#111827' },
  tdNum: { textAlign: 'right', fontVariant: ['tabular-nums'] },

  emptyNote: { fontSize: 13, color: '#9ca3af', marginTop: 16 },

  // disabled / empty states
  disabledTitle: { fontSize: 15, fontWeight: '700', color: '#374151', marginTop: 12, textAlign: 'center' },
  disabledNote: { fontSize: 13, color: '#9ca3af', marginTop: 8, textAlign: 'center', lineHeight: 19 },

  // saved list
  reportRow: {
    flexDirection: 'row', alignItems: 'center', backgroundColor: '#ffffff',
    borderRadius: 10, padding: 14, marginBottom: 8,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 4, elevation: 1,
  },
  reportName: { fontSize: 14.5, fontWeight: '600', color: '#111827' },
  reportMeta: { fontSize: 12, color: '#6b7280', marginTop: 3 },

  // modal
  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'center', paddingHorizontal: 36 },
  modalCard: { backgroundColor: '#ffffff', borderRadius: 16, padding: 22 },
  modalTitle: { fontSize: 16, fontWeight: '700', color: '#111827', marginBottom: 14 },
  input: {
    borderWidth: 1, borderColor: '#e5e7eb', borderRadius: 10, paddingHorizontal: 14,
    paddingVertical: 11, fontSize: 15, color: '#111827',
  },
  modalActions: { flexDirection: 'row', justifyContent: 'flex-end', gap: 10, marginTop: 18 },
  modalCancel: { paddingVertical: 10, paddingHorizontal: 16 },
  modalCancelText: { fontSize: 14, color: '#6b7280', fontWeight: '600' },
  modalSave: { backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 10, paddingHorizontal: 22, minWidth: 80, alignItems: 'center' },
  modalSaveText: { fontSize: 14, color: '#ffffff', fontWeight: '700' },
});
