import React, { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator, Alert, Modal, ScrollView, StyleSheet, Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useNavigation, useRoute } from '@react-navigation/native';
import { Ionicons } from '@expo/vector-icons';
import {
  Catalog, MeasureSpec, QueryResult, QuerySpec, SavedReport,
  createReport, getCatalog, runQuery, updateReport,
} from '../../services/analyticsService';
import { ChartType, ResultView } from '../../components/ReportChart';

const YEARS = ['2023', '2024', '2025', '2026', 'All'];
const CHART_TYPES: { id: ChartType; icon: any }[] = [
  { id: 'bar', icon: 'bar-chart-outline' },
  { id: 'line', icon: 'trending-up-outline' },
  { id: 'pie', icon: 'pie-chart-outline' },
  { id: 'table', icon: 'grid-outline' },
];

function buildSpec(measures: MeasureSpec[], dims: string[], year: string, completedOnly: boolean, chartType: ChartType): QuerySpec {
  const filters = [];
  if (year === 'All') filters.push({ field: 'order_date', op: 'gte', values: ['2023-01-01'] });
  else filters.push({ field: 'year', op: 'eq', values: [year] });
  if (completedOnly) filters.push({ field: 'payment_status', op: 'eq', values: ['completed'] });
  const orderBy = dims.length > 0 && measures.length > 0
    ? [{ field: `${measures[0].field}_${measures[0].agg}`, dir: 'desc' }] : undefined;
  return { measures, dimensions: dims, filters, orderBy, limit: 200, chartType };
}

type Sheet = 'measures' | 'dimensions' | 'time' | null;

export default function ExploreTab() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<any>();
  const route = useRoute<any>();

  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);

  const [measures, setMeasures] = useState<MeasureSpec[]>([{ field: 'line_revenue', agg: 'sum' }]);
  const [dims, setDims] = useState<string[]>(['category']);
  const [year, setYear] = useState('All');
  const [completedOnly, setCompletedOnly] = useState(true);
  const [chartType, setChartType] = useState<ChartType>('bar');

  const [result, setResult] = useState<QueryResult | null>(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);

  const [loadedReportId, setLoadedReportId] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [saveName, setSaveName] = useState('');
  const [saving, setSaving] = useState(false);

  const [sheet, setSheet] = useState<Sheet>(null);

  const measureLabel = useMemo(() => {
    const m: Record<string, string> = {};
    catalog?.measures.forEach(x => { m[x.id] = x.label; });
    return m;
  }, [catalog]);
  const dimLabel = useMemo(() => {
    const m: Record<string, string> = {};
    catalog?.dimensions.forEach(x => { m[x.id] = x.label; });
    return m;
  }, [catalog]);

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

  // Edit a saved report passed from the Analytics page.
  useEffect(() => {
    const rep: SavedReport | undefined = route.params?.report;
    if (rep) {
      loadReport(rep);
      navigation.setParams({ report: undefined });
    }
  }, [route.params?.report]);

  async function runSpec(spec: QuerySpec) {
    setRunning(true);
    setRunError(null);
    try {
      const res = await runQuery(spec);
      if (res.enabled === false) { setRunError('Analytics warehouse is not configured.'); setResult(null); }
      else setResult(res);
    } catch (e: any) {
      setRunError(e?.message ?? 'Query failed.');
      setResult(null);
    } finally {
      setRunning(false);
    }
  }

  function run() {
    if (measures.length === 0) { setRunError('Pick at least one measure.'); return; }
    runSpec(buildSpec(measures, dims, year, completedOnly, chartType));
  }

  function loadReport(rep: SavedReport) {
    const cfg = rep.config;
    setMeasures(cfg.measures ?? []);
    setDims(cfg.dimensions ?? []);
    const yf = cfg.filters?.find(f => f.field === 'year');
    const df = cfg.filters?.find(f => f.field === 'order_date');
    setYear(yf ? yf.values[0] : df ? 'All' : 'All');
    setCompletedOnly(!!cfg.filters?.some(f => f.field === 'payment_status'));
    setChartType((cfg.chartType as ChartType) ?? 'table');
    setLoadedReportId(rep.id);
    setSaveName(rep.name);
    runSpec(cfg);
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

  async function doSave(asNew: boolean) {
    if (!saveName.trim()) return;
    setSaving(true);
    try {
      const spec = buildSpec(measures, dims, year, completedOnly, chartType);
      if (loadedReportId && !asNew) await updateReport(loadedReportId, saveName.trim(), spec);
      else { const r = await createReport(saveName.trim(), spec); setLoadedReportId(r.id); }
      setSaveOpen(false);
    } catch (e: any) {
      Alert.alert('Save failed', e?.message ?? 'Could not save.');
    } finally {
      setSaving(false);
    }
  }

  // --- render -----------------------------------------------------------------
  if (catalogError) return <View style={styles.centered}><Text style={styles.error}>{catalogError}</Text></View>;
  if (!catalog) return <View style={styles.centered}><ActivityIndicator size="large" color="#16a34a" /></View>;
  if (!catalog.enabled) {
    return (
      <View style={[styles.centered, { paddingHorizontal: 32 }]}>
        <Ionicons name="cloud-offline-outline" size={40} color="#9ca3af" />
        <Text style={styles.disabledTitle}>Analytics warehouse not configured</Text>
        <Text style={styles.disabledNote}>The Explore builder needs the BigQuery warehouse.</Text>
      </View>
    );
  }

  return (
    <View style={styles.wrapper}>
      <View style={[styles.header, { paddingTop: insets.top + 14 }]}>
        <Text style={styles.headerTitle}>Explore</Text>
        <Text style={styles.headerSub}>Build a custom chart</Text>
      </View>

      <ScrollView style={styles.scroll} contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        {/* MEASURES */}
        <Text style={styles.label}>Measures</Text>
        <SelectedChips
          items={measures.map(m => ({ key: `${m.field}_${m.agg}`, text: `${measureLabel[m.field] ?? m.field} · ${m.agg.replace('_', ' ')}`, onRemove: () => toggleMeasure(m.field, m.agg) }))}
          placeholder="No measures"
        />
        <DropdownButton text="Add measure" onPress={() => setSheet('measures')} />

        {/* DIMENSIONS */}
        <Text style={[styles.label, styles.mt]}>Group by</Text>
        <SelectedChips
          items={dims.map(d => ({ key: d, text: dimLabel[d] ?? d, onRemove: () => toggleDim(d) }))}
          placeholder="No grouping (totals)"
        />
        <DropdownButton text={`Add dimension (${dims.length}/3)`} onPress={() => setSheet('dimensions')} />

        {/* TIME RANGE */}
        <Text style={[styles.label, styles.mt]}>Time range</Text>
        <DropdownButton text={year === 'All' ? 'All time' : year} onPress={() => setSheet('time')} value />

        <TouchableOpacity style={styles.toggleRow} onPress={() => setCompletedOnly(v => !v)} activeOpacity={0.7}>
          <Ionicons name={completedOnly ? 'checkbox' : 'square-outline'} size={20} color={completedOnly ? '#16a34a' : '#9ca3af'} />
          <Text style={styles.toggleText}>Completed orders only</Text>
        </TouchableOpacity>

        {/* CHART TYPE */}
        <Text style={[styles.label, styles.mt]}>Chart</Text>
        <View style={styles.segment}>
          {CHART_TYPES.map(ct => {
            const on = chartType === ct.id;
            return (
              <TouchableOpacity key={ct.id} style={[styles.segItem, on && styles.segItemOn]} onPress={() => setChartType(ct.id)} activeOpacity={0.8}>
                <Ionicons name={ct.icon} size={18} color={on ? '#ffffff' : '#6b7280'} />
                <Text style={[styles.segText, on && styles.segTextOn]}>{ct.id}</Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* ACTIONS */}
        <View style={styles.actions}>
          <TouchableOpacity style={styles.runBtn} onPress={run} disabled={running} activeOpacity={0.85}>
            {running ? <ActivityIndicator color="#fff" /> : <Text style={styles.runBtnText}>Run</Text>}
          </TouchableOpacity>
          <TouchableOpacity style={styles.saveBtn} onPress={() => { setSaveName(saveName || 'My report'); setSaveOpen(true); }} activeOpacity={0.85}>
            <Ionicons name="bookmark-outline" size={16} color="#14532d" />
            <Text style={styles.saveBtnText}>{loadedReportId ? 'Update' : 'Save'}</Text>
          </TouchableOpacity>
        </View>
        {loadedReportId && (
          <TouchableOpacity style={styles.detachRow} onPress={() => setLoadedReportId(null)} activeOpacity={0.7}>
            <Ionicons name="add-circle-outline" size={15} color="#6b7280" />
            <Text style={styles.detachText}>Editing a saved report — tap to start a new one</Text>
          </TouchableOpacity>
        )}

        {/* RESULT */}
        {runError && <Text style={styles.error}>{runError}</Text>}
        {result && <View style={styles.mt}><ResultView result={result} chartType={chartType} /></View>}
        <View style={{ height: 32 }} />
      </ScrollView>

      {/* PICKER SHEETS */}
      <PickerSheet visible={sheet === 'measures'} title="Measures" onClose={() => setSheet(null)}>
        {catalog.measures.map(m => (
          <View key={m.id} style={styles.sheetRow}>
            <Text style={styles.sheetRowLabel}>{m.label}</Text>
            <View style={styles.chipWrap}>
              {m.aggs.map(agg => {
                const on = measures.some(x => x.field === m.id && x.agg === agg);
                return (
                  <TouchableOpacity key={agg} style={[styles.chip, on && styles.chipOn]} onPress={() => toggleMeasure(m.id, agg)}>
                    <Text style={[styles.chipText, on && styles.chipTextOn]}>{agg.replace('_', ' ')}</Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          </View>
        ))}
      </PickerSheet>

      <PickerSheet visible={sheet === 'dimensions'} title={`Group by (${dims.length}/3)`} onClose={() => setSheet(null)}>
        <View style={styles.chipWrap}>
          {catalog.dimensions.map(d => {
            const on = dims.includes(d.id);
            return (
              <TouchableOpacity key={d.id} style={[styles.chip, on && styles.chipOn]} onPress={() => toggleDim(d.id)}>
                <Text style={[styles.chipText, on && styles.chipTextOn]}>{d.label}</Text>
              </TouchableOpacity>
            );
          })}
        </View>
      </PickerSheet>

      <PickerSheet visible={sheet === 'time'} title="Time range" onClose={() => setSheet(null)}>
        {YEARS.map(y => (
          <TouchableOpacity key={y} style={styles.optRow} onPress={() => { setYear(y); setSheet(null); }}>
            <Text style={styles.optText}>{y === 'All' ? 'All time' : y}</Text>
            {year === y && <Ionicons name="checkmark" size={18} color="#16a34a" />}
          </TouchableOpacity>
        ))}
      </PickerSheet>

      {/* SAVE MODAL */}
      <Modal visible={saveOpen} transparent animationType="fade" onRequestClose={() => setSaveOpen(false)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>{loadedReportId ? 'Update report' : 'Save report'}</Text>
            <TextInput style={styles.input} value={saveName} onChangeText={setSaveName} placeholder="Report name" placeholderTextColor="#9ca3af" autoFocus />
            <View style={styles.modalActions}>
              <TouchableOpacity onPress={() => setSaveOpen(false)} style={styles.modalGhost}><Text style={styles.modalGhostText}>Cancel</Text></TouchableOpacity>
              {loadedReportId && (
                <TouchableOpacity onPress={() => doSave(true)} style={styles.modalGhost} disabled={saving}><Text style={styles.modalNewText}>Save as new</Text></TouchableOpacity>
              )}
              <TouchableOpacity onPress={() => doSave(false)} style={styles.modalSave} disabled={saving}>
                {saving ? <ActivityIndicator color="#fff" /> : <Text style={styles.modalSaveText}>{loadedReportId ? 'Update' : 'Save'}</Text>}
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

// --- small building blocks ----------------------------------------------------
function SelectedChips({ items, placeholder }: { items: { key: string; text: string; onRemove: () => void }[]; placeholder: string }) {
  if (items.length === 0) return <Text style={styles.placeholder}>{placeholder}</Text>;
  return (
    <View style={styles.chipWrap}>
      {items.map(it => (
        <TouchableOpacity key={it.key} style={styles.selChip} onPress={it.onRemove} activeOpacity={0.7}>
          <Text style={styles.selChipText}>{it.text}</Text>
          <Ionicons name="close" size={13} color="#14532d" />
        </TouchableOpacity>
      ))}
    </View>
  );
}

function DropdownButton({ text, onPress, value }: { text: string; onPress: () => void; value?: boolean }) {
  return (
    <TouchableOpacity style={[styles.dropdown, value && styles.dropdownValue]} onPress={onPress} activeOpacity={0.7}>
      <Text style={[styles.dropdownText, value && styles.dropdownTextValue]}>{text}</Text>
      <Ionicons name="chevron-down" size={16} color="#6b7280" />
    </TouchableOpacity>
  );
}

function PickerSheet({ visible, title, onClose, children }: { visible: boolean; title: string; onClose: () => void; children: React.ReactNode }) {
  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View style={styles.sheetBackdrop}>
        <TouchableOpacity style={{ flex: 1 }} activeOpacity={1} onPress={onClose} />
        <View style={styles.sheet}>
          <View style={styles.sheetHead}>
            <Text style={styles.sheetTitle}>{title}</Text>
            <TouchableOpacity onPress={onClose}><Text style={styles.sheetDone}>Done</Text></TouchableOpacity>
          </View>
          <ScrollView style={{ maxHeight: 420 }} contentContainerStyle={{ paddingBottom: 12 }}>{children}</ScrollView>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  wrapper: { flex: 1, backgroundColor: '#f9fafb' },
  header: { paddingHorizontal: 24, paddingBottom: 14, backgroundColor: '#ffffff', borderBottomWidth: 1, borderBottomColor: '#f3f4f6' },
  headerTitle: { fontSize: 22, fontWeight: '700', color: '#111827' },
  headerSub: { fontSize: 13, color: '#6b7280', marginTop: 2 },
  scroll: { flex: 1 },
  content: { paddingHorizontal: 24, paddingTop: 18, paddingBottom: 32 },
  centered: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9fafb' },
  error: { color: '#dc2626', fontSize: 13, marginTop: 12 },

  label: { fontSize: 13, fontWeight: '700', color: '#374151', marginBottom: 8, textTransform: 'uppercase', letterSpacing: 0.4 },
  mt: { marginTop: 18 },
  placeholder: { fontSize: 13, color: '#9ca3af', marginBottom: 8 },

  chipWrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  chip: { paddingVertical: 6, paddingHorizontal: 12, borderRadius: 16, backgroundColor: '#ffffff', borderWidth: 1, borderColor: '#e5e7eb' },
  chipOn: { backgroundColor: '#14532d', borderColor: '#14532d' },
  chipText: { fontSize: 12.5, color: '#374151', fontWeight: '600' },
  chipTextOn: { color: '#ffffff' },

  selChip: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingVertical: 6, paddingHorizontal: 12, borderRadius: 16, backgroundColor: '#f0fdf4', borderWidth: 1, borderColor: '#bbf7d0', marginBottom: 2 },
  selChipText: { fontSize: 12.5, color: '#14532d', fontWeight: '600' },

  dropdown: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', backgroundColor: '#ffffff', borderWidth: 1, borderColor: '#e5e7eb', borderRadius: 10, paddingVertical: 11, paddingHorizontal: 14, marginTop: 8 },
  dropdownValue: { borderColor: '#d1d5db' },
  dropdownText: { fontSize: 14, color: '#6b7280', fontWeight: '600' },
  dropdownTextValue: { color: '#111827' },

  toggleRow: { flexDirection: 'row', alignItems: 'center', marginTop: 14, gap: 8 },
  toggleText: { fontSize: 13.5, color: '#374151', fontWeight: '500' },

  segment: { flexDirection: 'row', backgroundColor: '#eef2f1', borderRadius: 10, padding: 4, gap: 4 },
  segItem: { flex: 1, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 5, paddingVertical: 9, borderRadius: 8 },
  segItemOn: { backgroundColor: '#14532d' },
  segText: { fontSize: 12.5, color: '#6b7280', fontWeight: '600', textTransform: 'capitalize' },
  segTextOn: { color: '#ffffff' },

  actions: { flexDirection: 'row', gap: 12, marginTop: 24 },
  runBtn: { flex: 1, backgroundColor: '#16a34a', borderRadius: 12, paddingVertical: 14, alignItems: 'center', justifyContent: 'center' },
  runBtnText: { color: '#ffffff', fontSize: 15, fontWeight: '700' },
  saveBtn: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 18, borderRadius: 12, borderWidth: 1.5, borderColor: '#14532d', backgroundColor: '#f0fdf4' },
  saveBtnText: { color: '#14532d', fontSize: 14, fontWeight: '700' },
  detachRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 6, marginTop: 10 },
  detachText: { fontSize: 12, color: '#6b7280' },

  // sheets
  sheetBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)' },
  sheet: { backgroundColor: '#ffffff', borderTopLeftRadius: 20, borderTopRightRadius: 20, paddingHorizontal: 20, paddingTop: 14, paddingBottom: 28 },
  sheetHead: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 },
  sheetTitle: { fontSize: 16, fontWeight: '700', color: '#111827' },
  sheetDone: { fontSize: 15, fontWeight: '700', color: '#16a34a' },
  sheetRow: { marginBottom: 14 },
  sheetRowLabel: { fontSize: 13.5, fontWeight: '600', color: '#374151', marginBottom: 6 },
  optRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: 13, borderBottomWidth: 1, borderBottomColor: '#f3f4f6' },
  optText: { fontSize: 15, color: '#111827' },

  // modal
  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'center', paddingHorizontal: 36 },
  modalCard: { backgroundColor: '#ffffff', borderRadius: 16, padding: 22 },
  modalTitle: { fontSize: 16, fontWeight: '700', color: '#111827', marginBottom: 14 },
  input: { borderWidth: 1, borderColor: '#e5e7eb', borderRadius: 10, paddingHorizontal: 14, paddingVertical: 11, fontSize: 15, color: '#111827' },
  modalActions: { flexDirection: 'row', justifyContent: 'flex-end', alignItems: 'center', gap: 8, marginTop: 18 },
  modalGhost: { paddingVertical: 10, paddingHorizontal: 14 },
  modalGhostText: { fontSize: 14, color: '#6b7280', fontWeight: '600' },
  modalNewText: { fontSize: 14, color: '#14532d', fontWeight: '700' },
  modalSave: { backgroundColor: '#16a34a', borderRadius: 10, paddingVertical: 10, paddingHorizontal: 22, minWidth: 80, alignItems: 'center' },
  modalSaveText: { fontSize: 14, color: '#ffffff', fontWeight: '700' },

  disabledTitle: { fontSize: 15, fontWeight: '700', color: '#374151', marginTop: 12, textAlign: 'center' },
  disabledNote: { fontSize: 13, color: '#9ca3af', marginTop: 8, textAlign: 'center', lineHeight: 19 },
});
