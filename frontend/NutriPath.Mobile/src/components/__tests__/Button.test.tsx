import React from 'react';
import { describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { Button } from '@/components/Button';

describe('Button', () => {
  it('shows its label and calls onPress when tapped', async () => {
    const onPress = jest.fn();
    await render(<Button label="Save Goals" onPress={onPress} />);

    await fireEvent.press(screen.getByText('Save Goals'));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('renders the secondary variant', async () => {
    await render(<Button label="Log Out" variant="secondary" onPress={() => {}} />);

    expect(screen.getByText('Log Out')).toBeTruthy();
  });
});
