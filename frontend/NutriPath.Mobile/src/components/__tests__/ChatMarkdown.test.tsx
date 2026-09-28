import React from 'react';
import { describe, expect, it } from '@jest/globals';
import { render, screen } from '@testing-library/react-native';
import { ChatMarkdown } from '@/components/ChatMarkdown';

describe('ChatMarkdown', () => {
  it('shows bold, bullets and headings without the Markdown symbols', async () => {
    await render(
      <ChatMarkdown
        style={{}}
        text={'### Your week\n📊 **Score: 72**\n\n- Ate **dhal** 3 times\n* Hit protein twice\n1. Log breakfast\n---'}
      />
    );

    expect(screen.getByText('Your week')).toBeTruthy();
    expect(screen.getByText('Score: 72')).toBeTruthy();
    expect(screen.getByText('dhal')).toBeTruthy();
    expect(screen.getAllByText('•')).toHaveLength(2);
    expect(screen.getByText('1.')).toBeTruthy();
    expect(screen.queryByText(/\*\*|###|---/)).toBeNull();
  });

  it('leaves a lone star alone', async () => {
    await render(<ChatMarkdown style={{}} text="5 * 2 = 10" />);

    expect(screen.getByText('5 * 2 = 10')).toBeTruthy();
  });
});
