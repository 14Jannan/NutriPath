import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import * as Notifications from '@/notifications/localNotifications';
import { cancelMealReminders, syncMealReminders } from '@/notifications/mealReminders';

jest.mock('@/notifications/localNotifications', () => ({
  setNotificationHandler: jest.fn(),
  setNotificationChannelAsync: jest.fn(async () => null),
  getPermissionsAsync: jest.fn(async () => ({ granted: true, canAskAgain: true })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true })),
  getAllScheduledNotificationsAsync: jest.fn(async () => []),
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

  it('replaces earlier reminders but leaves other notifications alone', async () => {
    notifications.getAllScheduledNotificationsAsync.mockResolvedValue([
      { identifier: 'meal-reminder:2026-09-26:Lunch' },
      { identifier: 'something-else' },
    ] as never);

    await cancelMealReminders();

    expect(notifications.cancelScheduledNotificationAsync).toHaveBeenCalledTimes(1);
    expect(notifications.cancelScheduledNotificationAsync).toHaveBeenCalledWith('meal-reminder:2026-09-26:Lunch');
  });

  it('schedules nothing if notifications are turned off', async () => {
    notifications.getPermissionsAsync.mockResolvedValue({ granted: false, canAskAgain: false } as never);

    await syncMealReminders([], now);

    expect(notifications.scheduleNotificationAsync).not.toHaveBeenCalled();
  });
});
