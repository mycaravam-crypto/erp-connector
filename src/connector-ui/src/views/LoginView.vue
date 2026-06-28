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
    await router.push({ name: 'exports' })
  } else {
    error.value = result.error ?? 'Login failed.'
  }
}
</script>

<template>
  <div class="login-wrap">
    <div class="login-card">
      <h1>Connector — Sign in</h1>
      <p class="subtitle">Release UI requires authentication.</p>

      <div class="field">
        <label for="username">Username</label>
        <input
          id="username"
          v-model="username"
          autocomplete="username"
          @keyup.enter="submit"
        />
      </div>

      <div class="field">
        <label for="password">Password</label>
        <input
          id="password"
          v-model="password"
          type="password"
          autocomplete="current-password"
          @keyup.enter="submit"
        />
      </div>

      <p v-if="error" class="error">{{ error }}</p>

      <button
        @click="submit"
        :disabled="!username.trim() || !password || submitting"
      >
        {{ submitting ? 'Signing in…' : 'Sign in' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.login-wrap {
  display: flex;
  justify-content: center;
  padding-top: 6rem;
}

.login-card {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  padding: 2rem;
  width: 100%;
  max-width: 360px;
}

h1 {
  margin: 0 0 0.25rem;
  font-size: 1.1rem;
}

.subtitle {
  color: #64748b;
  font-size: 0.85rem;
  margin: 0 0 1.5rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.75rem;
}

label {
  font-size: 0.85rem;
  font-weight: 600;
}

input {
  padding: 0.4rem 0.6rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  outline: none;
}

input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 2px #e0e7ff;
}

button {
  margin-top: 0.5rem;
  width: 100%;
  padding: 0.5rem;
  background: #4f46e5;
  color: #fff;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.error {
  color: #dc2626;
  font-size: 0.85rem;
  margin: 0.25rem 0;
}
</style>
