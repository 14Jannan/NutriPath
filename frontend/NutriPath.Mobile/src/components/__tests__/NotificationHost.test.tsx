import React from 'react';
import { describe, expect, it, jest } from '@jest/globals';
import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { NotificationHost } from '@/components/NotificationHost';
import { confirm, notify } from '@/notifications/notify';

// The first render loads the icon font library, which can take several
// seconds when the whole suite runs in parallel.
jest.setTimeout(30000);

// Fixed safe-area values, since tests have no real screen to measure.
const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };

async function renderHost() {
  await render(
    <SafeAreaProvider initialMetrics={metrics}>
      <NotificationHost />
    </SafeAreaProvider>
  );
}

describe('NotificationHost', () => {
  it('shows a notification in the app, and dismisses it on tap', async () => {
    await renderHost();

    await act(async () => notify('error', 'Could not save', "Can't reach the server."));
    expect(screen.getByText('Could not save')).toBeTruthy();
    expect(screen.getByText("Can't reach the server.")).toBeTruthy();

    await fireEvent.press(screen.getByText('Could not save'));
    expect(screen.queryByText('Could not save')).toBeNull();
  });

  it('asks for confirmation in the app and reports the answer', async () => {
    await renderHost();

    let answer: Promise<boolean> = Promise.resolve(false);
    await act(async () => {
      answer = confirm('Remove this item?', 'Rice (150g)', 'Remove', true);
    });
    expect(screen.getByText('Remove this item?')).toBeTruthy();

    await fireEvent.press(screen.getByText('Remove'));
    await expect(answer).resolves.toBe(true);
    expect(screen.queryByText('Remove this item?')).toBeNull();
  });

  it('treats Cancel as no', async () => {
    await renderHost();

    let answer: Promise<boolean> = Promise.resolve(true);
    await act(async () => {
      answer = confirm('Start a new chat?', 'This conversation will be cleared.', 'New chat');
    });
    await fireEvent.press(screen.getByText('Cancel'));

    await expect(answer).resolves.toBe(false);
  });
});
