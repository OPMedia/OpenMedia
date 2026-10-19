/** 认证 store:token 持久化(localStorage),SPECAPI 双通道鉴权由 client.ts 承担。 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authenticateByName, getPublicUsers, getUser } from '@/api/endpoints'
import { setToken, getToken } from '@/api/client'
import type { UserDto } from '@/api/types'

const USER_KEY = 'serata.user'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserDto | null>(JSON.parse(localStorage.getItem(USER_KEY) ?? 'null'))
  const token = ref<string | null>(getToken())

  const isLoggedIn = computed(() => !!token.value && !!user.value)

  async function login(username: string, password: string): Promise<UserDto> {
    const result = await authenticateByName(username, password)
    token.value = result.AccessToken
    user.value = result.User
    setToken(result.AccessToken)
    localStorage.setItem(USER_KEY, JSON.stringify(result.User))
    return result.User
  }

  async function refreshUser(): Promise<void> {
    if (token.value && user.value) user.value = await getUser(user.value.Id)
  }

  function logout(): void {
    token.value = null
    user.value = null
    setToken(null)
    localStorage.removeItem(USER_KEY)
  }

  async function fetchPublicUsers(): Promise<UserDto[]> {
    return getPublicUsers()
  }

  return { user, token, isLoggedIn, login, logout, refreshUser, fetchPublicUsers }
})