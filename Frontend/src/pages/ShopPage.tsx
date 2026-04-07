import { useEffect, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { InventoryItem, RevenueReport, ShopItem, UserProfile } from '../types'

interface ShopPageProps {
  user: UserProfile
  onUserRefresh: () => Promise<void>
}

export function ShopPage({ user, onUserRefresh }: ShopPageProps) {
  const [items, setItems] = useState<ShopItem[]>([])
  const [inventory, setInventory] = useState<InventoryItem[]>([])
  const [feedback, setFeedback] = useState('')
  const [report, setReport] = useState<RevenueReport | null>(null)
  const currentMonth = new Date().toISOString().slice(0, 7)

  useEffect(() => {
    void loadShop()
  }, [user.id])

  async function loadShop() {
    const [shopItems, userInventory, monthlyReport] = await Promise.allSettled([
      api.getShopItems(),
      api.getInventory(user.id),
      api.getRevenue(currentMonth),
    ])

    if (shopItems.status === 'fulfilled') {
      setItems(shopItems.value)
    }

    if (userInventory.status === 'fulfilled') {
      setInventory(userInventory.value)
    }

    if (monthlyReport.status === 'fulfilled') {
      setReport(monthlyReport.value)
    } else {
      setReport(null)
    }
  }

  async function buyItem(itemId: string) {
    try {
      const result = await api.buyItem(user.id, itemId)
      setFeedback(result.message)
      await loadShop()
      await onUserRefresh()
    } catch (caughtError) {
      setFeedback(caughtError instanceof Error ? caughtError.message : 'Kupovina nije uspela.')
    }
  }

  async function sellItem(itemId: string, purchasedAt: string) {
    try {
      const result = await api.sellItem(user.id, itemId, purchasedAt)
      setFeedback(result.message)
      await loadShop()
      await onUserRefresh()
    } catch (caughtError) {
      setFeedback(caughtError instanceof Error ? caughtError.message : 'Prodaja nije uspela.')
    }
  }

  return (
    <>
      <SectionPanel title="Prodavnica">
        {feedback ? <p className="feedback">{feedback}</p> : null}

        <div className="cards-grid">
          {items.map((item) => (
            <article key={item.id} className="shop-card">
              <span className="shop-card__badge">
                {item.isLimited ? 'Limited edition' : 'Standard item'}
              </span>
              <h3>{item.name}</h3>
              <p>{item.price} coins</p>
              {item.isLimited ? (
                <span className="muted">
                  Stock: {item.currentStock}/{item.initialStock}
                </span>
              ) : null}
              <button className="button" onClick={() => buyItem(item.id)}>
                Kupi predmet
              </button>
            </article>
          ))}
        </div>
      </SectionPanel>

      <SectionPanel title="Inventar">
        <div className="list-stack">
          {inventory.length > 0 ? (
            inventory.map((item) => (
              <article key={`${item.itemId}-${item.purchasedAt}`} className="list-item">
                <div>
                  <strong>{item.itemName}</strong>
                  <span>Kupljeno za {item.purchasePrice} coins</span>
                  <span>Prodajna vrednost: {item.resalePrice} coins</span>
                </div>
                <div className="inventory-actions">
                  <span>{new Date(item.purchasedAt).toLocaleString()}</span>
                  <button
                    className="button button--ghost"
                    onClick={() => void sellItem(item.itemId, item.purchasedAt)}
                  >
                    Prodaj skin
                  </button>
                </div>
              </article>
            ))
          ) : (
            <p className="muted">Inventar je prazan. Kupi prvi predmet u shop-u.</p>
          )}
        </div>
      </SectionPanel>

      <SectionPanel title="Najprodavaniji predmet ovog meseca">
        {report ? (
          <div className="dual-grid">
            <div className="nested-card">
              <h3>Najprodavaniji predmet</h3>
              <strong>{report.bestSellingItem}</strong>
              <span>Broj prodaja: {report.salesCount}</span>
              <span>Mesec: {report.month}</span>
            </div>
          </div>
        ) : (
          <p className="muted">Podaci o prodaji za tekuci mesec nisu dostupni.</p>
        )}
      </SectionPanel>
    </>
  )
}
