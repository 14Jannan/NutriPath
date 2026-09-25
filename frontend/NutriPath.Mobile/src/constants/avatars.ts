import type { MaterialCommunityIcons } from '@expo/vector-icons';

// The fixed set of avatars. IDs must match AvatarCatalog.Ids on the
// backend (backend/NutriPath.Api/Models/AvatarCatalog.cs), which only
// accepts these values.

type IconName = keyof typeof MaterialCommunityIcons.glyphMap;

export interface AvatarOption {
  id: string;
  label: string;
  icon: IconName;
  background: string;
  foreground: string;
}

export interface AvatarGroup {
  title: string;
  avatars: AvatarOption[];
}

const a = (id: string, label: string, icon: IconName, background: string, foreground: string): AvatarOption => ({
  id,
  label,
  icon,
  background,
  foreground,
});

export const AVATAR_GROUPS: AvatarGroup[] = [
  {
    title: 'Fruit and vegetables',
    avatars: [
      a('apple', 'Apple', 'food-apple', '#FDE2E1', '#C62828'),
      a('carrot', 'Carrot', 'carrot', '#FFE8D1', '#E65100'),
      a('watermelon', 'Watermelon', 'fruit-watermelon', '#E3F6E5', '#2E7D32'),
      a('pineapple', 'Pineapple', 'fruit-pineapple', '#FFF4CC', '#A66A00'),
      a('cherries', 'Cherries', 'fruit-cherries', '#FCE4EC', '#AD1457'),
      a('grapes', 'Grapes', 'fruit-grapes', '#EDE7F6', '#5E35B1'),
      a('citrus', 'Lime', 'fruit-citrus', '#EEF7D6', '#558B2F'),
      a('corn', 'Corn', 'corn', '#FFF6D5', '#B28704'),
    ],
  },
  {
    title: 'Animals',
    avatars: [
      a('cat', 'Cat', 'cat', '#FBE9E7', '#BF360C'),
      a('dog', 'Dog', 'dog', '#EFEBE9', '#6D4C41'),
      a('panda', 'Panda', 'panda', '#ECEFF1', '#263238'),
      a('penguin', 'Penguin', 'penguin', '#E1F5FE', '#01579B'),
      a('owl', 'Owl', 'owl', '#F3E5F5', '#6A1B9A'),
      a('rabbit', 'Rabbit', 'rabbit', '#FCE4EC', '#880E4F'),
      a('turtle', 'Turtle', 'turtle', '#E0F2F1', '#00695C'),
      a('koala', 'Koala', 'koala', '#ECEFF1', '#455A64'),
      a('elephant', 'Elephant', 'elephant', '#E8EAF6', '#3949AB'),
      a('bee', 'Bee', 'bee', '#FFF8E1', '#8D6E00'),
      a('butterfly', 'Butterfly', 'butterfly', '#E3F2FD', '#1565C0'),
      a('duck', 'Duck', 'duck', '#FFFDE7', '#9E7C00'),
    ],
  },
  {
    title: 'Active and nature',
    avatars: [
      a('leaf', 'Leaf', 'leaf', '#E6F8ED', '#154539'),
      a('sprout', 'Sprout', 'sprout', '#EAF7E0', '#33691E'),
      a('runner', 'Runner', 'run', '#E0F7FA', '#00838F'),
      a('dumbbell', 'Dumbbell', 'dumbbell', '#EDE7F6', '#4527A0'),
    ],
  },
];

export const AVATARS: AvatarOption[] = AVATAR_GROUPS.flatMap((g) => g.avatars);

/** The avatar for an ID, or undefined for none/unknown (the Avatar component shows a fallback). */
export function getAvatar(id: string | null | undefined): AvatarOption | undefined {
  return id ? AVATARS.find((av) => av.id === id) : undefined;
}
