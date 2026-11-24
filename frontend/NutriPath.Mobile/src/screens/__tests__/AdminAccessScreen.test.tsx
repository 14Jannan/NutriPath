import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { Linking } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ADMIN_MESSAGE, AdminAccessScreen, adminPortalUrl } from '@/screens/AdminAccessScreen';

jest.setTimeout(30000); // the first render loads the icon library

const mockLogout = jest.fn(async () => {});
jest.mock('@/context/AuthContext', () => ({ useAuth: () => ({ logoutUser: mockLogout }) }));

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const renderScreen = () => render(<SafeAreaProvider initialMetrics={metrics}><AdminAccessScreen /></SafeAreaProvider>);

beforeEach(() => {
  jest.clearAllMocks();
  delete process.env.EXPO_PUBLIC_ADMIN_PORTAL_URL;
});

describe('AdminAccessScreen', () => {
  it('shows the administrator message and signs out', async () => {
    await renderScreen();
    expect(screen.getByText('Administrator Access')).toBeTruthy();
    expect(screen.getByText(ADMIN_MESSAGE)).toBeTruthy();
    fireEvent.press(screen.getByRole('button', { name: 'Sign out' }));
    expect(mockLogout).toHaveBeenCalled();
  });

  it('opens the configured portal URL', async () => {
    process.env.EXPO_PUBLIC_ADMIN_PORTAL_URL = 'https://admin.nutripath.example';
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true as never);
    await renderScreen();
    fireEvent.press(screen.getByRole('button', { name: 'Open Admin Portal' }));
    expect(open).toHaveBeenCalledWith('https://admin.nutripath.example/');
  });

  it('disables the portal button when no URL is configured', async () => {
    await renderScreen();
    expect(screen.getByRole('button', { name: 'Open Admin Portal' })).toBeDisabled();
  });
});

describe('adminPortalUrl', () => {
  it('accepts https and rejects other schemes', () => {
    expect(adminPortalUrl('https://admin.example.org', false)).toBe('https://admin.example.org/');
    expect(adminPortalUrl('http://admin.example.org', false)).toBeNull();
    expect(adminPortalUrl('javascript:alert(1)', true)).toBeNull();
    expect(adminPortalUrl('not a url', true)).toBeNull();
    expect(adminPortalUrl(undefined, true)).toBeNull();
  });

  it('allows plain http only for local hosts in development', () => {
    expect(adminPortalUrl('http://192.168.1.8:5173', true)).toBe('http://192.168.1.8:5173/');
    expect(adminPortalUrl('http://192.168.1.8:5173', false)).toBeNull();
    expect(adminPortalUrl('http://evil.example', true)).toBeNull();
  });
});
