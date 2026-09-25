import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import * as foodsApi from '@/api/foodsApi';
import { AddFoodScreen } from '@/screens/AddFoodScreen';
import { FoodSearchScreen } from '@/screens/FoodSearchScreen';

jest.setTimeout(30000); // the first render loads the icon library

const mockNavigation = { navigate: jest.fn(), replace: jest.fn(), goBack: jest.fn() };
let mockParams: Record<string, unknown> = {};
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => mockNavigation,
  useRoute: () => ({ params: mockParams }),
}));

jest.mock('@/api/foodsApi', () => ({ createFood: jest.fn(), searchFoods: jest.fn() }));
const api = jest.mocked(foodsApi);

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };
const wrap = (ui: React.ReactElement) => <SafeAreaProvider initialMetrics={metrics}>{ui}</SafeAreaProvider>;
const saveButton = () => screen.getByRole('button', { name: 'Save and choose portion' });
const disabled = () => saveButton().props.accessibilityState?.disabled === true;

beforeEach(() => {
  jest.clearAllMocks();
  mockParams = { mealType: 'Dinner', date: '2026-09-25', name: 'Chicken kottu' };
});

describe('AddFoodScreen', () => {
  it('pre-fills the searched name and saves a private food, then opens its portion screen', async () => {
    api.createFood.mockResolvedValue({ id: 'f1', name: 'Chicken kottu' } as never);
    await render(wrap(<AddFoodScreen />));

    expect(screen.getByDisplayValue('Chicken kottu')).toBeTruthy();
    expect(disabled()).toBe(true); // nutrients still missing

    await fireEvent.changeText(screen.getByLabelText('Calories'), '205');
    await fireEvent.changeText(screen.getByLabelText('Protein'), '7');
    await fireEvent.changeText(screen.getByLabelText('Carbs'), '25');
    await fireEvent.changeText(screen.getByLabelText('Fat'), '8');
    expect(screen.getByText('Protein, carbs and fat add up to about 200 kcal.')).toBeTruthy();

    await fireEvent.press(saveButton());

    await waitFor(() =>
      expect(mockNavigation.replace).toHaveBeenCalledWith('FoodDetail', { foodId: 'f1', mealType: 'Dinner', date: '2026-09-25' })
    );
    expect(api.createFood).toHaveBeenCalledWith({
      name: 'Chicken kottu',
      calories: 205,
      proteinGrams: 7,
      carbsGrams: 25,
      fatGrams: 8,
      fiberGrams: undefined,
      sugarGrams: undefined,
      sodiumMilligrams: undefined,
    });
  });

  it('blocks saving when calories clearly do not match the macros', async () => {
    await render(wrap(<AddFoodScreen />));

    await fireEvent.changeText(screen.getByLabelText('Calories'), '30'); // typo for ~200
    await fireEvent.changeText(screen.getByLabelText('Protein'), '7');
    await fireEvent.changeText(screen.getByLabelText('Carbs'), '25');
    await fireEvent.changeText(screen.getByLabelText('Fat'), '8');

    expect(screen.getByText(/doesn't match/)).toBeTruthy();
    expect(disabled()).toBe(true);
  });
});

describe('FoodSearchScreen', () => {
  it('offers to add a food the database does not have', async () => {
    mockParams = { mealType: 'Dinner', date: '2026-09-25' };
    api.searchFoods.mockResolvedValue([]);
    await render(wrap(<FoodSearchScreen />));

    await fireEvent.changeText(screen.getByPlaceholderText('Search foods (e.g. rice, dhal, chicken)'), 'kottu');
    const add = await screen.findByText('Add "kottu" as your own food', {}, { timeout: 3000 });
    await fireEvent.press(add);

    expect(mockNavigation.navigate).toHaveBeenCalledWith('AddFood', { mealType: 'Dinner', date: '2026-09-25', name: 'kottu' });
  });
});
