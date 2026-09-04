<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    type?: 'button' | 'submit'
    disabled?: boolean
    loading?: boolean
  }>(),
  { variant: 'primary', type: 'button', disabled: false, loading: false },
)

const base =
  'inline-flex items-center justify-center gap-1.5 rounded-md px-3.5 py-1.5 text-sm font-semibold ' +
  'transition-colors duration-fast cursor-pointer disabled:cursor-not-allowed disabled:opacity-50 ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-surface'

const variants: Record<string, string> = {
  primary: `${base} bg-brand text-white border border-transparent hover:enabled:bg-brand-hover active:enabled:bg-brand-active`,
  secondary: `${base} bg-surface text-text-primary border border-border-strong hover:enabled:bg-surface-elevated`,
  ghost: `${base} bg-transparent text-text-secondary border border-transparent hover:enabled:bg-surface-elevated hover:enabled:text-text-primary`,
  danger: `${base} bg-danger-solid text-white border border-transparent hover:enabled:bg-danger-solid-hover active:enabled:bg-danger-solid-active`,
}
</script>

<template>
  <button :type="type" :class="variants[variant]" :disabled="disabled || loading">
    <span
      v-if="loading"
      class="size-3.5 shrink-0 rounded-full border-2 border-current border-t-transparent animate-spin"
      aria-hidden="true"
    />
    <slot v-if="$slots.icon && !loading" name="icon" />
    <slot />
  </button>
</template>
