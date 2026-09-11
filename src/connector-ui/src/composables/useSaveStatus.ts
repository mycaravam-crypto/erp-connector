import { ref } from 'vue'

/** The saving/saveStatus/saveMessage triad duplicated identically across several definition/settings
 * save forms — factored out once a third copy (SchedulerSettingsForm.vue) pushed the clone past
 * fallow's duplicate-detection threshold. */
export function useSaveStatus() {
  const saving = ref(false)
  const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
  const saveMessage = ref('')

  function reset() {
    saveStatus.value = 'idle'
    saveMessage.value = ''
  }

  return { saving, saveStatus, saveMessage, reset }
}
