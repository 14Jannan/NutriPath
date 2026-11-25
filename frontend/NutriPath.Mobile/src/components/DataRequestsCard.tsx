import React, { useCallback, useEffect, useState } from 'react';
import { Share, StyleSheet, Text, View } from 'react-native';
import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { describeApiError } from '@/api/client';
import { cancelDataRequest, createDataRequest, downloadExport, listDataRequests, type DataRequest, type DataRequestType } from '@/api/accountApi';
import { useAppStatus } from '@/hooks/useAppStatus';
import { confirmAction, showAlert } from '@/utils/alert';
import { colors, spacing, typography } from '@/theme';

const describe = (r: DataRequest) => {
  const what = r.type === 'Export' ? 'Copy of your data' : 'Account deletion';
  switch (r.status) {
    case 'Pending': return `${what}: requested, waiting for review`;
    case 'Completed': return r.type === 'Export' ? (r.downloadAvailable ? `${what}: ready to download` : `${what}: expired`) : `${what}: done`;
    case 'Rejected': return `${what}: declined${r.adminNote ? ` (${r.adminNote})` : ''}`;
    default: return `${what}: cancelled`;
  }
};

/** Privacy requests: a copy of the user's data, or deleting the account. Reviewed by NutriPath staff. */
export function DataRequestsCard() {
  const { isEnabled, status } = useAppStatus();
  const [requests, setRequests] = useState<DataRequest[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    listDataRequests().then(setRequests).catch(() => setRequests([]));
  }, []);
  useEffect(load, [load]);

  if (!isEnabled('privacy.selfServiceRequests')) {
    return (
      <Card style={styles.card}>
        <Text style={styles.title}>Your data</Text>
        <Text style={styles.body}>To get a copy of your data or delete your account, contact {status?.supportEmail ?? 'NutriPath support'}.</Text>
      </Card>
    );
  }

  const pending = (type: DataRequestType) => requests.some((r) => r.type === type && r.status === 'Pending');

  function request(type: DataRequestType) {
    confirmAction(
      type === 'Export' ? 'Request a copy of your data?' : 'Request account deletion?',
      type === 'Export'
        ? 'We will prepare a file with your profile, meal logs, custom foods and assistant conversations. You can download it here once it is ready.'
        : 'Once approved, your account and all its data are permanently deleted. This cannot be undone.',
      type === 'Export' ? 'Request' : 'Request deletion',
      async () => {
        setBusy(true);
        try {
          await createDataRequest(type);
          showAlert(type === 'Export' ? 'Request sent' : 'Deletion requested', 'We will review it shortly.', 'success');
          load();
        } catch (error) {
          showAlert("Couldn't send the request", describeApiError(error));
        } finally {
          setBusy(false);
        }
      },
      type === 'Deletion',
    );
  }

  async function download(r: DataRequest) {
    try {
      const json = await downloadExport(r.id);
      await Share.share({ title: 'NutriPath data export', message: json });
    } catch (error) {
      showAlert("Couldn't download your data", describeApiError(error));
    }
  }

  async function cancel(r: DataRequest) {
    try {
      await cancelDataRequest(r.id);
      load();
    } catch (error) {
      showAlert("Couldn't cancel the request", describeApiError(error));
    }
  }

  return (
    <Card style={styles.card}>
      <View style={styles.header}>
        <MaterialCommunityIcons name="shield-lock-outline" size={20} color={colors.primary} />
        <Text style={styles.title}>Your data</Text>
      </View>
      <Text style={styles.body}>Get a copy of everything NutriPath stores about you, or ask us to delete your account.</Text>
      {requests.slice(0, 3).map((r) => (
        <View key={r.id} style={styles.requestRow}>
          <Text style={styles.requestText}>{describe(r)}</Text>
          {r.downloadAvailable && <Button label="Download" variant="secondary" onPress={() => void download(r)} style={styles.small} />}
          {r.status === 'Pending' && <Button label="Cancel" variant="secondary" onPress={() => void cancel(r)} style={styles.small} />}
        </View>
      ))}
      <Button label="Request a copy of my data" variant="secondary" disabled={busy || pending('Export')} onPress={() => request('Export')} style={styles.button} />
      <Button label="Delete my account" variant="danger" disabled={busy || pending('Deletion')} onPress={() => request('Deletion')} style={styles.button} />
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { gap: spacing.sm },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  title: { ...typography.labelLg, color: colors.onSurface },
  body: { ...typography.bodySm, color: colors.onSurfaceVariant },
  requestRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm },
  requestText: { ...typography.bodySm, color: colors.onSurface, flex: 1 },
  small: { height: 36, paddingHorizontal: spacing.md },
  button: { marginTop: spacing.xs },
});
