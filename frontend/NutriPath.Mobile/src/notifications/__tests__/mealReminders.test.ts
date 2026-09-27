import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import * as Notifications from '@/notifications/localNotifications';
import { cancelMealReminders, syncMealReminders } from '@/notifications/mealReminders';

jest.mock('@/notifications/localNotifications', () => ({
  setNotificationHandler: jest.fn(),
  setNotificationChannelAsync: jest.fn(async () => null),
  getPermissionsAsync: jest.fn(async () => ({ granted: true, canAskAgain: true })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true })),
  cancelScheduledNotificationAsync: jest.fn(async () => {}),
  scheduleNotificationAsync: jest.fn(async () => 'id'),
  AndroidImportance: { HIGH: 4 },
  SchedulableTriggerInputTypes: { DATE: 'date' },
}));
const notifications = jest.mocked(Notifications);

// Saturday 26 Sep 2026, 1 pm: breakfast's window has closed, lunch is open.
const now = new Date(2026, 8, 26, 13, 0);

function scheduledIds() {
  return notifications.scheduleNotificationAsync.mock.calls.map(([request]) => request.identifier);
}

beforeEach(() => {
  jest.clearAllMocks();
});

describe('meal reminders', () => {
  it("reminds for today's meals still to close, except the ones already logged", async () => {
    await syncMealReminders(['Lunch'], now);

    const today = scheduledIds().filter((id) => id!.includes('2026-09-26'));
    // Breakfast has passed, lunch is logged.
    expect(today).toEqual(['meal-reminder:2026-09-26:Snack', 'meal-reminder:2026-09-26:Dinner']);
    // Every meal on each of the next 6 days.
    expect(scheduledIds()).toHaveLength(2 + 6 * 4);
    expect(notifications.scheduleNotificationAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        identifier: 'meal-reminder:2026-09-26:Dinner',
        trigger: expect.objectContaining({ date: new Date(2026, 8, 26, 22, 0) }),
      })
    );
  });

  it('cancels reminders for meals logged today, and keeps going if one fails', async () => {
    notifications.scheduleNotificationAsync.mockRejectedValueOnce(new Error('alarm limit'));

    await syncMealReminders(['Lunch'], now);

    // Breakfast is past and lunch is logged, so theirs are removed.
    expect(notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith('meal-reminder:2026-09-26:Breakfast');
    expect(notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith('meal-reminder:2026-09-26:Lunch');
    // The first schedule failed, but every other one was still attempted.
    expect(scheduledIds()).toHaveLength(2 + 6 * 4);
  });

  it('runs overlapping syncs one after another', async () => {
    let running = 0;
    let maxRunning = 0;
    notifications.scheduleNotificationAsync.mockImplementation(async () => {
      maxRunning = Math.max(maxRunning, ++running);
      await Promise.resolve();
      running--;
      return 'id';
    });

    await Promise.all([syncMealReminders([], now), syncMealReminders([], now), syncMealReminders([], now)]);

    expect(maxRunning).toBe(1);
  });

  it('cancels only its own reminders by id on logout, without listing all notifications', async () => {
    await cancelMealReminders(now);

    const ids = notifications.cancelScheduledNotificationAsync.mock.calls.map(([id]) => id);
    expect(ids).toContain('meal-reminder:2026-09-26:Dinner');
    expect(ids.every((id) => id.startsWith('meal-reminder:'))).toBe(true);
  });

  it('schedules nothing if notifications are turned off', async () => {
    notifications.getPermissionsAsync.mockResolvedValue({ granted: false, canAskAgain: false } as never);

    await syncMealReminders([], now);

    expect(notifications.scheduleNotificationAsync).not.toHaveBeenCalled();
  });
});
