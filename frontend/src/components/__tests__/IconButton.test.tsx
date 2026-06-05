import React from 'react';
import { Text } from 'react-native';
import { fireEvent, render } from '@testing-library/react-native';

import IconButton from '../a11y/IconButton';

// useTheme throws outside its provider; the button only reads colors.accent.
jest.mock('../../providers/ThemeProvider', () => ({
  useTheme: () => ({ colors: { accent: '#06f' } }),
}));

describe('IconButton', () => {
  it('Should_ExposeButtonRoleAndLabel_When_Rendered', () => {
    const { getByLabelText } = render(
      <IconButton label="Close" onPress={() => {}}>
        <Text>×</Text>
      </IconButton>,
    );
    const node = getByLabelText('Close');
    expect(node.props.accessibilityRole).toBe('button');
  });

  it('Should_ReflectSelectedAndBusy_When_StateProvided', () => {
    const { getByLabelText } = render(
      <IconButton label="Bold" selected busy onPress={() => {}} />,
    );
    const node = getByLabelText('Bold');
    expect(node.props.accessibilityState).toMatchObject({ selected: true, busy: true });
  });

  it('Should_OverrideRole_When_AccessibilityRoleGiven', () => {
    const { getByLabelText } = render(
      <IconButton label="Light" accessibilityRole="radio" selected onPress={() => {}} />,
    );
    expect(getByLabelText('Light').props.accessibilityRole).toBe('radio');
  });

  it('Should_NotFireOnPress_When_Disabled', () => {
    const onPress = jest.fn();
    const { getByLabelText } = render(
      <IconButton label="Save" disabled onPress={onPress} />,
    );
    fireEvent.press(getByLabelText('Save'));
    expect(onPress).not.toHaveBeenCalled();
    expect(getByLabelText('Save').props.accessibilityState).toMatchObject({ disabled: true });
  });
});
