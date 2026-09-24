import React from 'react';
import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import * as profileApi from '@/api/profileApi';
import { OnboardingScreen } from '@/screens/OnboardingScreen';

jest.setTimeout(30000); // the first render loads the icon library

const mockAuth = {
  fullName: 'Jannan H',
  logoutUser: jest.fn(),
  markProfileComplete: jest.fn(),
};
jest.mock('@/context/AuthContext', () => ({ useAuth: () => mockAuth }));

jest.mock('@/api/profileApi', () => ({
  updateGoals: jest.fn(),
  previewGoals: jest.fn(),
  getProfileInsight: jest.fn(),
}));
const api = jest.mocked(profileApi);

const preview = {
  targetCalories: 2062,
  targetProteinGrams: 86,
  targetCarbsGrams: 301,
  targetFatGrams: 57,
  targetFiberGrams: 29,
  bmi: 18.1,
  bmiCategory: 'Underweight',
  healthyWeightMinKg: 55,
  healthyWeightMaxKg: 74,
};

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };

beforeEach(() => {
  jest.clearAllMocks();
  api.previewGoals.mockResolvedValue(preview);
  api.getProfileInsight.mockResolvedValue({ preview, insight: '• Add eggs or dhal to breakfast.' });
  api.updateGoals.mockResolvedValue({} as never);
});

const continueButton = () => screen.getByRole('button', { name: 'Continue' });
const isDisabled = (el: ReturnType<typeof continueButton>) => el.props.accessibilityState?.disabled === true;

describe('OnboardingScreen', () => {
  it('requires every answer, then saves the profile and unlocks the app', async () => {
    await render(
      <SafeAreaProvider initialMetrics={metrics}>
        <OnboardingScreen />
      </SafeAreaProvider>
    );

    // Welcome, greeting the user by first name.
    expect(screen.getByText('Hi Jannan!')).toBeTruthy();
    await fireEvent.press(screen.getByText("Let's get started"));

    // About you: blocked until both sex and a valid age are given.
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.press(screen.getByLabelText('Male'));
    await fireEvent.changeText(screen.getByLabelText('Age'), '5');
    expect(screen.getByText('Age must be between 13 and 120.')).toBeTruthy();
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Age'), '23');
    expect(isDisabled(continueButton())).toBe(false);
    await fireEvent.press(continueButton());

    // Body: an impossible pair is blocked; a real one shows BMI guidance.
    await fireEvent.changeText(screen.getByLabelText('Height'), '172');
    await fireEvent.changeText(screen.getByLabelText('Weight'), '25');
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.changeText(screen.getByLabelText('Weight'), '53.5');
    expect(screen.getByText('Healthy weight for 172 cm: 55–74 kg')).toBeTruthy();
    expect(screen.getByText('BMI 18.1 · Underweight')).toBeTruthy();
    await fireEvent.press(continueButton());

    // Activity and goal: a choice is required.
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.press(screen.getByLabelText('Lightly active'));
    await fireEvent.press(continueButton());
    await fireEvent.press(screen.getByLabelText('Lose weight'));
    // Losing weight with a low BMI gets a gentle warning.
    expect(screen.getByText(/losing weight isn't usually recommended/)).toBeTruthy();
    await fireEvent.press(screen.getByLabelText('Maintain'));
    await fireEvent.press(continueButton());

    // Food needs: both questions need an answer; "Other" needs text.
    await fireEvent.press(screen.getByText('Peanuts'));
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.press(screen.getAllByText('Other')[1]);
    expect(isDisabled(continueButton())).toBe(true);
    await fireEvent.changeText(screen.getByPlaceholderText('Please specify, e.g. no seafood'), 'no seafood');
    await fireEvent.press(continueButton());

    // Summary: live targets and the AI analysis appear, then start.
    await waitFor(() => expect(screen.getByText('2,062 kcal')).toBeTruthy());
    await waitFor(() => expect(screen.getByText('• Add eggs or dhal to breakfast.')).toBeTruthy(), { timeout: 3000 });
    await fireEvent.press(screen.getByText('Start my journey'));

    await waitFor(() => expect(mockAuth.markProfileComplete).toHaveBeenCalled());
    expect(api.updateGoals).toHaveBeenCalledWith({
      age: 23,
      sex: 'Male',
      heightCm: 172,
      weightKg: 53.5,
      activityLevel: 'Light',
      goal: 'Maintain',
      allergies: ['Peanuts'],
      dietaryPreferences: ['no seafood'],
    });
  });
});
