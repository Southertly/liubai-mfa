export interface AppColors {
  background: string;
  surface: string;
  surfaceRaised: string;
  foreground: string;
  mutedForeground: string;
  primary: string;
  primaryPressed: string;
  primarySoft: string;
  border: string;
  input: string;
  success: string;
  warning: string;
  destructive: string;
  scrim: string;
}

export interface LabelColorOption {
  id: string;
  value: string;
  label: string;
}

export const LIGHT_COLORS: AppColors = {
  background: '#F4F5F7',
  surface: '#FFFFFF',
  surfaceRaised: '#FFFFFF',
  foreground: '#111318',
  mutedForeground: '#69707D',
  primary: '#316FF6',
  primaryPressed: '#2459C8',
  primarySoft: '#E8EFFF',
  border: '#DFE3EA',
  input: '#EEF0F4',
  success: '#168A54',
  warning: '#B26A00',
  destructive: '#D92D20',
  scrim: '#66000000'
};

export const DARK_COLORS: AppColors = {
  background: '#0D0F14',
  surface: '#191C24',
  surfaceRaised: '#20242E',
  foreground: '#F2F4F8',
  mutedForeground: '#9AA2B1',
  primary: '#5B8CFF',
  primaryPressed: '#7CA4FF',
  primarySoft: '#202C48',
  border: '#303541',
  input: '#242833',
  success: '#45C68A',
  warning: '#F0B35A',
  destructive: '#FF6B66',
  scrim: '#99000000'
};

export const LABEL_COLORS: LabelColorOption[] = [
  { id: 'blue', value: '#4F7EF8', label: '蓝色' },
  { id: 'teal', value: '#14B8A6', label: '青色' },
  { id: 'green', value: '#22C55E', label: '绿色' },
  { id: 'amber', value: '#F59E0B', label: '黄色' },
  { id: 'orange', value: '#F97316', label: '橙色' },
  { id: 'red', value: '#EF4444', label: '红色' },
  { id: 'pink', value: '#EC4899', label: '粉色' },
  { id: 'purple', value: '#8B5CF6', label: '紫色' },
  { id: 'slate', value: '#64748B', label: '灰色' }
];

export function colorsFor(dark: boolean): AppColors {
  return dark ? DARK_COLORS : LIGHT_COLORS;
}

export function labelColor(colorId: string, fallback: string): string {
  for (const option of LABEL_COLORS) {
    if (option.id === colorId) {
      return option.value;
    }
  }
  return fallback;
}
