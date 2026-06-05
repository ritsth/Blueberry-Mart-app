import React, { useEffect, useState } from 'react';
import { ActivityIndicator, Dimensions, ScrollView, StyleSheet, Text, TouchableOpacity, View } from 'react-native';
import { BarChart, LineChart, PieChart } from 'react-native-chart-kit';
import { Ionicons } from '@expo/vector-icons';
import { QueryResult, SavedReport, runQuery } from '../services/analyticsService';

const SCREEN_WIDTH = Dimensions.get('window').width;
export const CHART_WIDTH = SCREEN_WIDTH - 48;

export type ChartType = 'bar' | 'line' | 'pie' | 'table';

export const chartConfig = {
  backgroundGradientFrom: '#ffffff',
  backgroundGradientTo: '#ffffff',
  decimalPlaces: 0,
  color: (opacity = 1) => `rgba(20, 83, 45, ${opacity})`,
  labelColor: (opacity = 1) => `rgba(107, 114, 128, ${opacity})`,
  propsForDots: { r: '3', strokeWidth: '2', stroke: '#16a34a' },
  barPercentage: 0.6,
};

export const PIE_COLORS = ['#16a34a', '#0284c7', '#d97706', '#7c3aed', '#dc2626', '#0d9488', '#db2777', '#65a30d'];

// --- formatting ---------------------------------------------------------------
function fmtCell(v: any): string {
  if (v === null || v === undefined) return '—';
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  if (typeof v === 'number') return v.toLocaleString('en-NP', { maximumFractionDigits: 2 });
  return String(v);
}
function shortLabel(v: any): string {
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  const s = String(v).replace('Blueberry Mart ', '');
  return s.length > 10 ? s.slice(0, 9) + '…' : s;
}
function toNum(v: any): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

// --- chart + table ------------------------------------------------------------
export function ResultView({ result, chartType }: { result: QueryResult; chartType: ChartType }) {
  const dimensionCols = result.columns.filter(c => c.role === 'dimension');
  const measureCols = result.columns.filter(c => c.role === 'measure');
  const rows = result.rows;

  if (rows.length === 0) return <Text style={styles.empty}>No rows for this selection.</Text>;

  const canChart = chartType !== 'table' && dimensionCols.length >= 1 && measureCols.length >= 1;

  return (
    <View>
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
    <View style={styles.chartWrap}>
      {chartType === 'bar' && (
        <BarChart data={{ labels, datasets: [{ data }] }} width={CHART_WIDTH} height={230}
          chartConfig={chartConfig} fromZero showValuesOnTopOfBars yAxisLabel="" yAxisSuffix="" style={styles.chart} />
      )}
      {chartType === 'line' && (
        <LineChart data={{ labels, datasets: [{ data }] }} width={CHART_WIDTH} height={230}
          chartConfig={chartConfig} bezier fromZero yAxisLabel="" yAxisSuffix="" style={styles.chart} />
      )}
      {chartType === 'pie' && (
        <PieChart
          data={slice.map((r, i) => ({
            name: shortLabel(r[dimKey]), population: Math.max(0, toNum(r[measureKey])),
            color: PIE_COLORS[i % PIE_COLORS.length], legendFontColor: '#374151', legendFontSize: 12,
          }))}
          width={CHART_WIDTH} height={200} chartConfig={chartConfig}
          accessor="population" backgroundColor="transparent" paddingLeft="12" />
      )}
      {rows.length > cap && <Text style={styles.trunc}>Showing top {cap} of {rows.length}</Text>}
      <Text style={styles.caption}>{measureLabel}</Text>
    </View>
  );
}

function DataTable({ columns, rows }: { columns: QueryResult['columns']; rows: Record<string, any>[] }) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 4 }}>
      <View>
        <View style={[styles.tr, styles.trHead]}>
          {columns.map(c => (
            <Text key={c.key} style={[styles.th, c.role === 'measure' && styles.num]} numberOfLines={1}>{c.label}</Text>
          ))}
        </View>
        {rows.slice(0, 100).map((r, i) => (
          <View key={i} style={[styles.tr, i % 2 === 1 && styles.trAlt]}>
            {columns.map(c => (
              <Text key={c.key} style={[styles.td, c.role === 'measure' && styles.num]} numberOfLines={1}>{fmtCell(r[c.key])}</Text>
            ))}
          </View>
        ))}
      </View>
    </ScrollView>
  );
}

// --- self-fetching saved report card (used on the Analytics page) -------------
export function SavedReportCard({
  report, onEdit, onDelete,
}: { report: SavedReport; onEdit: () => void; onDelete: () => void }) {
  const [result, setResult] = useState<QueryResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    (async () => {
      setLoading(true);
      try {
        const r = await runQuery(report.config);
        if (!alive) return;
        if (r.enabled === false) setError('Warehouse not configured.');
        else setResult(r);
      } catch (e: any) {
        if (alive) setError(e?.message ?? 'Failed to load.');
      } finally {
        if (alive) setLoading(false);
      }
    })();
    return () => { alive = false; };
  }, [report.id]);

  const chartType = (report.config.chartType as ChartType) ?? 'table';

  return (
    <View style={styles.card}>
      <View style={styles.cardHead}>
        <Text style={styles.cardName} numberOfLines={1}>{report.name}</Text>
        <View style={styles.cardActions}>
          <TouchableOpacity onPress={onEdit} hitSlop={hit}><Ionicons name="create-outline" size={18} color="#6b7280" /></TouchableOpacity>
          <TouchableOpacity onPress={onDelete} hitSlop={hit}><Ionicons name="trash-outline" size={18} color="#dc2626" /></TouchableOpacity>
        </View>
      </View>
      {loading
        ? <View style={styles.cardLoading}><ActivityIndicator color="#16a34a" /></View>
        : error
          ? <Text style={styles.empty}>{error}</Text>
          : result && <ResultView result={result} chartType={chartType} />}
    </View>
  );
}

const hit = { top: 8, bottom: 8, left: 8, right: 8 };

const styles = StyleSheet.create({
  chartWrap: {
    backgroundColor: '#ffffff', borderRadius: 14, paddingVertical: 12, marginBottom: 12,
    alignItems: 'center', overflow: 'hidden',
  },
  chart: { borderRadius: 12 },
  caption: { fontSize: 12, color: '#6b7280', fontWeight: '600', marginTop: 2 },
  trunc: { fontSize: 11, color: '#9ca3af', marginTop: 2 },
  tr: { flexDirection: 'row' },
  trHead: { borderBottomWidth: 1.5, borderBottomColor: '#e5e7eb', paddingBottom: 6, marginBottom: 4 },
  trAlt: { backgroundColor: '#f9fafb' },
  th: { minWidth: 96, paddingHorizontal: 8, fontSize: 12, fontWeight: '700', color: '#374151' },
  td: { minWidth: 96, paddingHorizontal: 8, paddingVertical: 5, fontSize: 12.5, color: '#111827' },
  num: { textAlign: 'right', fontVariant: ['tabular-nums'] },
  empty: { fontSize: 13, color: '#9ca3af', marginVertical: 12 },
  card: {
    backgroundColor: '#ffffff', borderRadius: 14, padding: 14, marginBottom: 12,
    shadowColor: '#000', shadowOffset: { width: 0, height: 1 }, shadowOpacity: 0.05, shadowRadius: 6, elevation: 1,
  },
  cardHead: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 },
  cardName: { flex: 1, fontSize: 14.5, fontWeight: '700', color: '#111827' },
  cardActions: { flexDirection: 'row', gap: 16, marginLeft: 8 },
  cardLoading: { paddingVertical: 24, alignItems: 'center' },
});
