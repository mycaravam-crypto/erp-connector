<template>
  <div class="flex flex-col items-center justify-center min-h-64 gap-4 text-center">
    <span class="text-5xl font-bold text-slate-300">404</span>
    <p class="text-slate-500 text-sm">Page not found.</p>
    <RouterLink
      :to="{ name: 'exports' }"
      class="text-sm text-slate-400 underline hover:text-slate-200 transition-colors"
    >
      Back to Export Runs
    </RouterLink>
  </div>
</template>
