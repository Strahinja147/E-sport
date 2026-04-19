const APP_LOCALE = 'sr-RS'
const APP_TIME_ZONE = 'Europe/Belgrade'

function normalizeDate(value: string | Date) {
  return value instanceof Date ? value : new Date(value)
}

function formatWithOptions(value: string | Date, options: Intl.DateTimeFormatOptions) {
  return new Intl.DateTimeFormat(APP_LOCALE, {
    timeZone: APP_TIME_ZONE,
    ...options,
  }).format(normalizeDate(value))
}

function getDayKey(value: string | Date) {
  const date = normalizeDate(value)
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: APP_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date)

  const year = parts.find((part) => part.type === 'year')?.value ?? '0000'
  const month = parts.find((part) => part.type === 'month')?.value ?? '00'
  const day = parts.find((part) => part.type === 'day')?.value ?? '00'

  return `${year}-${month}-${day}`
}

export function isSameAppDay(left: string | Date, right: string | Date) {
  return getDayKey(left) === getDayKey(right)
}

export function formatAppDate(value: string | Date) {
  return formatWithOptions(value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  })
}

export function formatAppTime(value: string | Date) {
  return formatWithOptions(value, {
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function formatAppDateTime(value: string | Date) {
  return formatWithOptions(value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
