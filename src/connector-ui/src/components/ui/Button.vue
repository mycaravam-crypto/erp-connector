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
  'transition-all duration-fast cursor-pointer disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:translate-y-0 ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-surface'

// primary/danger carry a bit of lift + a tinted glow on hover — the solid,
// filled variants read as the page's main actions, so they get the extra
// tactility; secondary/ghost stay flat to keep that emphasis meaningful.
const variants: Record<string, string> = {
  primary: `${base} bg-brand text-white border border-transparent shadow-sm hover:enabled:bg-brand-hover hover:enabled:shadow-glow hover:enabled:-translate-y-0.5 active:enabled:bg-brand-active active:enabled:translate-y-0`,
  secondary: `${base} bg-surface text-text-primary border border-border-strong hover:enabled:bg-surface-elevated`,
  ghost: `${base} bg-transparent text-text-secondary border border-transparent hover:enabled:bg-surface-elevated hover:enabled:text-text-primary`,
  danger: `${base} bg-danger-solid text-white border border-transparent shadow-sm hover:enabled:bg-danger-solid-hover hover:enabled:shadow-glow hover:enabled:-translate-y-0.5 active:enabled:bg-danger-solid-active active:enabled:translate-y-0`,
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
