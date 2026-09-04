<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { login } from '@/api/auth'
import logo from '@/assets/logo.svg'
import Card from '@/components/ui/Card.vue'
import Input from '@/components/ui/Input.vue'
import Button from '@/components/ui/Button.vue'

const router = useRouter()

const username = ref('')
const password = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)

async function submit() {
  if (!username.value.trim() || !password.value) return
  submitting.value = true
  error.value = null
  const result = await login(username.value.trim(), password.value)
  submitting.value = false
  if (result.ok) {
    await router.push({ name: 'connect' })
  } else {
    error.value = result.error ?? 'Login failed.'
  }
}
</script>

<template>
  <div class="flex justify-center pt-24">
    <Card class="w-full max-w-sm">
      <div class="flex items-center gap-2.5 mb-4">
        <img :src="logo" alt="" class="w-8 h-8 rounded-lg" />
        <h1 class="text-lg font-semibold m-0 text-text-primary">X5 Connector</h1>
      </div>
      <p class="text-text-secondary text-sm m-0 mb-6">Release UI requires authentication.</p>

      <Input id="username" v-model="username" label="Username" autocomplete="username" class="mb-3" @keyup.enter="submit" />
      <Input id="password" v-model="password" type="password" label="Password" autocomplete="current-password" class="mb-3" @keyup.enter="submit" />

      <p v-if="error" class="text-danger text-sm mt-1 mb-0">{{ error }}</p>

      <Button
        class="mt-3 w-full justify-center"
        :disabled="!username.trim() || !password || submitting"
        :loading="submitting"
        @click="submit"
      >
        {{ submitting ? 'Signing in…' : 'Sign in' }}
      </Button>
    </Card>
  </div>
</template>
