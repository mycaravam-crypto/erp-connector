<script setup lang="ts">
import { ref } from 'vue'
import { Check, Copy } from 'lucide-vue-next'
import { regenerateConnectorInstanceId, type ConnectorInstance } from '@/api/instance'
import Button from '@/components/ui/Button.vue'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'
import Icon from '@/components/ui/Icon.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'
import { useToasts } from '@/composables/useToasts'

// Identity of this connector installation: the instance ID every export manifest carries, so an operator can
// tell which installation a connector-to-connector import ("From connector: …") came from. The ID can be
// regenerated for an installation set up from a copy of another's database (#233).
const instance = defineModel<ConnectorInstance>('instance', { required: true })
const toasts = useToasts()

const confirmingRegenerate = ref(false)
const regenerating = ref(false)
async function regenerate() {
  regenerating.value = true
  try {
    instance.value = await regenerateConnectorInstanceId()
    confirmingRegenerate.value = false
    toasts.success('Instance ID regenerated.')
  } catch {
    toasts.error('Could not regenerate the instance ID.')
  } finally {
    regenerating.value = false
  }
}

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
            original's ID, and each would then reject the other's files as its own export. Regenerate the ID on
            the copy; a paired instance then sees a new producer ID for this installation's exports.
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
    <div class="mt-3 flex flex-wrap items-center gap-2">
      <ConfirmAction
        v-model:confirming="confirmingRegenerate"
        :busy="regenerating"
        prompt="Paired connector instances will see a new producer ID. Regenerate?"
        confirm-label="Regenerate"
        @confirm="regenerate"
      >
        <template #trigger="{ open }">
          <Button variant="secondary" data-testid="regenerate-instance-id" @click="open">
            Regenerate instance ID
          </Button>
        </template>
      </ConfirmAction>
    </div>
  </section>
</template>
