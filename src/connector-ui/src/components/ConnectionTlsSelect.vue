<script setup lang="ts">
import Select from '@/components/ui/Select.vue'

// TLS mode for a relational source. Both providers store the same names (the backend maps them for MariaDB);
// only the labels and the offered set differ — MariaDB has no "Allow".
defineProps<{ sourceType: 'postgres' | 'mariadb' }>()
const model = defineModel<string>({ default: '' })
</script>

<template>
  <Select
    v-if="sourceType === 'postgres'"
    id="ssl-mode"
    v-model="model"
    label="TLS / SSL Mode"
    help-text="Prefer (default) uses TLS if the server offers it but silently falls back to an unencrypted connection otherwise. For a production ERP, use Require or, for full certificate verification, VerifyFull."
  >
    <option value="">Prefer (default)</option>
    <option value="Disable">Disable — never use TLS</option>
    <option value="Allow">Allow — TLS only if the client requests it</option>
    <option value="Require">Require — TLS mandatory, no certificate verification</option>
    <option value="VerifyCA">VerifyCA — TLS mandatory, verify the server's CA</option>
    <option value="VerifyFull">VerifyFull — TLS mandatory, verify CA and hostname</option>
  </Select>
  <Select
    v-else
    id="ssl-mode"
    v-model="model"
    label="TLS Mode"
    help-text="Preferred (default) uses TLS if the server offers it but falls back to an unencrypted connection otherwise. For a production ERP, use Required or, for full certificate verification, VerifyFull."
  >
    <option value="">Preferred (default)</option>
    <option value="Disable">None — never use TLS</option>
    <option value="Require">Required — TLS mandatory, no certificate verification</option>
    <option value="VerifyCA">VerifyCA — TLS mandatory, verify the server's CA</option>
    <option value="VerifyFull">VerifyFull — TLS mandatory, verify CA and hostname</option>
  </Select>
</template>
