<template>
  <div class="flex flex-col items-center justify-center min-h-64 gap-4 text-center">
    <span class="text-5xl font-bold text-text-muted">404</span>
    <p class="text-text-secondary text-sm">Page not found.</p>
    <RouterLink
      :to="{ name: 'exports' }"
      class="text-sm text-brand underline hover:text-brand-hover transition-colors"
    >
      Back to Export Runs
    </RouterLink>
  </div>
</template>
