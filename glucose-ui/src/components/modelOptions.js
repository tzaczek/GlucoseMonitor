/**
 * Shared GPT model options for one-off model override selectors.
 * The empty-string entry means "use the default from settings".
 */
const MODEL_OPTIONS = [
  { value: '', label: 'Default (from settings)' },
  { value: 'gpt-5.2', label: 'GPT-5.2' },
  { value: 'gpt-5-mini', label: 'GPT-5 Mini' },
  { value: 'gpt-4.1', label: 'GPT-4.1' },
  { value: 'gpt-4.1-mini', label: 'GPT-4.1 Mini' },
  { value: 'gpt-4.1-nano', label: 'GPT-4.1 Nano' },
  { value: 'o4-mini', label: 'o4-mini' },
  { value: 'o3-mini', label: 'o3-mini' },
  { value: 'gpt-4o', label: 'GPT-4o' },
  { value: 'gpt-4o-mini', label: 'GPT-4o Mini' },
  { value: 'gpt-4-turbo', label: 'GPT-4 Turbo' },
];

export default MODEL_OPTIONS;
