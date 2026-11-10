/** A value and its label, for selects and for showing enum values from the API. */
export interface Option<T extends string> {
  value: T;
  label: string;
}

/** The label for a value, or the value itself if it's unknown (e.g. a new server value). */
export function labelOf<T extends string>(options: readonly Option<T>[], value: string): string {
  return options.find((option) => option.value === value)?.label ?? value;
}
