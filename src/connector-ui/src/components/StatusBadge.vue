<script setup lang="ts">
/**
 * Ampel-Badge für den Export-Status.
 *
 * Zeigt "Pending" (gelb), "Released" (grün) oder "Failed" (rot) an.
 * Die Farbgebung folgt der Konvention aus TECHNICAL_CONCEPT.md §4.2.
 */
defineProps<{ status: string }>()
</script>

<template>
  <span :class="['badge', `badge-${status.toLowerCase()}`]">{{ status }}</span>
</template>

<style scoped>
.badge {
  display: inline-block;
  padding: 0.15rem 0.5rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-pending  { background: #fef9c3; color: #854d0e; }
.badge-released { background: #dcfce7; color: #166534; }
.badge-failed   { background: #fee2e2; color: #991b1b; }
</style>
