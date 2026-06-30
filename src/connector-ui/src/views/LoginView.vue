<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { login } from '@/api/auth'

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
    <div class="border border-slate-200 rounded-lg p-8 w-full max-w-sm">
      <h1 class="text-lg font-semibold m-0 mb-1">Connector — Sign in</h1>
      <p class="text-slate-500 text-sm m-0 mb-6">Release UI requires authentication.</p>

      <div class="flex flex-col gap-1 mb-3">
        <label for="username" class="text-sm font-semibold">Username</label>
        <input
          id="username"
          v-model="username"
          autocomplete="username"
          class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
          @keyup.enter="submit"
        />
      </div>

      <div class="flex flex-col gap-1 mb-3">
        <label for="password" class="text-sm font-semibold">Password</label>
        <input
          id="password"
          v-model="password"
          type="password"
          autocomplete="current-password"
          class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
          @keyup.enter="submit"
        />
      </div>

      <p v-if="error" class="text-red-600 text-sm mt-1 mb-0">{{ error }}</p>

      <button
        class="mt-3 w-full py-2 bg-indigo-600 text-white border-0 rounded-md text-sm font-semibold cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed hover:enabled:bg-indigo-700"
        :disabled="!username.trim() || !password || submitting"
        @click="submit"
      >
        {{ submitting ? 'Signing in…' : 'Sign in' }}
      </button>
    </div>
  </div>
</template>
