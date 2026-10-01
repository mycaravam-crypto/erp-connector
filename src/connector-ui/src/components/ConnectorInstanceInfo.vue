<script setup lang="ts">
import { ref } from 'vue'
import { Check, Copy } from 'lucide-vue-next'
import type { ConnectorInstance } from '@/api/instance'
import Icon from '@/components/ui/Icon.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'

// Read-only identity of this connector installation: the instance ID every export manifest carries, so an
// operator can tell which installation a connector-to-connector import ("From connector: …") came from.
defineProps<{ instance: ConnectorInstance }>()

const copied = ref(false)
async function copyId(id: string) {
  await navigator.clipboard.writeText(id)
  copied.value = true
  setTimeout(() => {
    copied.value = false
  }, 1500)
}
</script>

<template>
  <section>
    <SectionHeader title="Connector Instance">
      <template #help>
        <HelpTooltip label="What is the instance ID?" title="Connector instance ID">
          <p>
            Generated once when this installation first starts and stored in its database. Every export
            manifest carries it, and an import from another connector shows it as "From connector".
          </p>
          <p>
            Two installations must not share an ID: one set up from a copy of another's database keeps the
            original's ID, and each would then reject the other's files as its own export.
          </p>
        </HelpTooltip>
      </template>
    </SectionHeader>
    <dl class="mt-3 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm">
      <dt class="text-text-secondary">Instance ID</dt>
      <dd class="m-0">
        <button
          class="group inline-flex items-center gap-1.5 bg-transparent border-0 p-0 cursor-pointer"
          :title="copied ? 'Copied!' : 'Click to copy'"
          data-testid="instance-id"
          @click="copyId(instance.instanceId)"
        >
          <code class="text-xs break-all text-text-primary">{{ instance.instanceId }}</code>
          <span class="text-text-muted group-hover:text-text-secondary shrink-0">
            <Icon :icon="copied ? Check : Copy" :size="16" />
          </span>
        </button>
      </dd>
      <dt class="text-text-secondary">Application</dt>
      <dd class="m-0 text-text-primary">{{ instance.application }} {{ instance.version }}</dd>
    </dl>
  </section>
</template>
