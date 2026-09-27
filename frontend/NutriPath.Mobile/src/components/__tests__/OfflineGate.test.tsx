import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import * as Network from 'expo-network';
import { OfflineGate, isOffline } from '@/components/OfflineGate';

jest.setTimeout(30000); // the first render loads the icon library

jest.mock('expo-network', () => ({
  useNetworkState: jest.fn(),
  getNetworkStateAsync: jest.fn(),
}));
const network = jest.mocked(Network);

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const renderGate = () =>
  render(
    <SafeAreaProvider initialMetrics={metrics}>
      <OfflineGate />
    </SafeAreaProvider>
  );

beforeEach(() => {
  jest.clearAllMocks();
});

describe('OfflineGate', () => {
  it('only treats a definite "no" as offline', () => {
    expect(isOffline({ isConnected: false })).toBe(true);
    expect(isOffline({ isConnected: true, isInternetReachable: false })).toBe(true); // Wi-Fi without internet
    expect(isOffline({ isConnected: true, isInternetReachable: true })).toBe(false);
    expect(isOffline({})).toBe(false); // not known yet
  });

  it('shows nothing while online', async () => {
    network.useNetworkState.mockReturnValue({ isConnected: true, isInternetReachable: true });
    await renderGate();

    expect(screen.queryByText('No internet connection')).toBeNull();
  });

  it('blocks the app while offline, and says so if a retry is still offline', async () => {
    network.useNetworkState.mockReturnValue({ isConnected: false, isInternetReachable: false });
    network.getNetworkStateAsync.mockResolvedValue({ isConnected: false, isInternetReachable: false });
    await renderGate();

    expect(screen.getByText('No internet connection')).toBeTruthy();
    await fireEvent.press(screen.getByText('Try Again'));
    expect(await screen.findByText('Still offline. Check your connection.')).toBeTruthy();
  });

  it('explains Wi-Fi that has no internet', async () => {
    network.useNetworkState.mockReturnValue({ isConnected: true, isInternetReachable: false });
    await renderGate();

    expect(screen.getByText(/connected, but it has no internet/)).toBeTruthy();
  });
});
