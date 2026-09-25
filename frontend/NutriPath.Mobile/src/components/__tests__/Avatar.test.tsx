import React from 'react';
import { readFileSync } from 'fs';
import { join } from 'path';
import { describe, expect, it, jest } from '@jest/globals';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { Avatar } from '@/components/Avatar';
import { AvatarPickerSheet } from '@/components/AvatarPickerSheet';
import { AVATARS, getAvatar } from '@/constants/avatars';

jest.setTimeout(30000); // the first render loads the icon library

const metrics = { frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } };

describe('avatar catalog', () => {
  it('matches the backend list exactly, so every avatar can be saved', () => {
    // The server only accepts IDs from AvatarCatalog.cs; read them from there.
    const csharp = readFileSync(
      join(__dirname, '../../../../../backend/NutriPath.Api/Models/AvatarCatalog.cs'),
      'utf8'
    );
    const idsBlock = csharp.slice(csharp.indexOf('Ids = new[]'), csharp.indexOf('};'));
    const backendIds = [...idsBlock.matchAll(/"([^"]+)"/g)].map((m) => m[1]);

    expect(AVATARS.map((a) => a.id)).toEqual(backendIds);
  });

  it('has unique ids and falls back for unknown ones', () => {
    expect(new Set(AVATARS.map((a) => a.id)).size).toBe(AVATARS.length);
    expect(getAvatar('panda')?.label).toBe('Panda');
    expect(getAvatar('no-such-avatar')).toBeUndefined();
    expect(getAvatar(null)).toBeUndefined();
  });
});

describe('Avatar', () => {
  it('shows the chosen avatar, or a default when none is set', async () => {
    await render(
      <>
        <Avatar avatarId="panda" />
        <Avatar avatarId={null} />
      </>
    );

    expect(screen.getByLabelText('Panda avatar')).toBeTruthy();
    expect(screen.getByLabelText('Default avatar')).toBeTruthy();
  });
});

describe('AvatarPickerSheet', () => {
  it('saves a newly picked avatar, and only when it changed', async () => {
    const onSave = jest.fn();
    await render(
      <SafeAreaProvider initialMetrics={metrics}>
        <AvatarPickerSheet visible current="panda" saving={false} onClose={() => {}} onSave={onSave} />
      </SafeAreaProvider>
    );

    const save = () => screen.getByRole('button', { name: 'Save avatar' });
    expect(save().props.accessibilityState?.disabled).toBe(true); // nothing changed yet

    await fireEvent.press(screen.getByLabelText('Carrot'));
    expect(save().props.accessibilityState?.disabled).toBe(false);
    await fireEvent.press(save());

    expect(onSave).toHaveBeenCalledWith('carrot');
  });
});
