import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { apiClient } from '@/api/client';
import { getDailyMeals, logMealItem } from '@/api/mealsApi';
import { toLocalIsoDate } from '@/utils/date';

// The real client talks to the backend and secure storage; these tests
// only check what the API functions send.
jest.mock('@/api/client', () => ({
  apiClient: { get: jest.fn(), post: jest.fn(), delete: jest.fn() },
}));

const mockedClient = jest.mocked(apiClient);

beforeEach(() => {
  jest.clearAllMocks();
});

describe('mealsApi', () => {
  it('logs an item into the given local date', async () => {
    mockedClient.post.mockResolvedValue({ data: {} } as never);

    await logMealItem('food-1', 150, 'Lunch', '2026-09-23');

    expect(mockedClient.post).toHaveBeenCalledWith('/api/meals/items', {
      foodId: 'food-1',
      quantityGrams: 150,
      mealType: 'Lunch',
      date: '2026-09-23',
    });
  });

  it("defaults to today's LOCAL date, not the UTC one", async () => {
    mockedClient.get.mockResolvedValue({ data: { meals: [] } } as never);

    await getDailyMeals();

    expect(mockedClient.get).toHaveBeenCalledWith('/api/meals/daily', {
      params: { date: toLocalIsoDate(new Date()) },
    });
  });
});
