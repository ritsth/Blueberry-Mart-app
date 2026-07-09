using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;
using BlueberryMart.Api.Services.Interfaces;
using BlueberryMart.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Back-office order fulfillment for store staff. Branch-scoped like inventory
/// management: staff/managers act only on their own branch's orders; admins on any.
/// Cancellation is manager/admin only.
/// </summary>
[ApiController]
[Route("api/orders/manage")]
[Authorize(Roles = "Staff,Manager,Admin")]
public class ManageOrdersController(
    BlueberryMartDbContext context,
    ISalesEventOutbox salesEvents,
    IOrderCancellationService cancellation,
    IStockEventProducer stockEvents,
    ISettingsService settings) : ControllerBase
{
    // Linear forward fulfillment chain (paid orders only — pending is advanced by
    // recording a payment, not here). Cancellation has its own manager-only endpoint.
    private static readonly Dictionary<string, string> NextStatus = new()
    {
        ["confirmed"] = "processing",
        ["processing"] = "ready",
        ["ready"] = "completed",
    };

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? CallerBranch() =>
        Guid.TryParse(User.FindFirstValue("branch"), out var b) ? b : null;

    private ActionResult? GuardBranch(Guid branchId)
    {
        if (IsAdmin) return null;
        var mine = CallerBranch();
        if (mine is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
        if (mine.Value != branchId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage orders in your own branch." });
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<ManagedOrderPage>> List(
        [FromQuery] Guid? branchId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.Orders.AsNoTracking().AsQueryable();

        if (!IsAdmin)
        {
            var mine = CallerBranch();
            if (mine is null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
            query = query.Where(o => o.BranchId == mine.Value);
        }
        else if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status.ToLower());
        if (!string.IsNullOrWhiteSpace(search) && int.TryParse(search.Trim().TrimStart('#'), out var num))
            query = query.Where(o => o.OrderNumber == num);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new ManagedOrderResponse
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerEmail = o.User != null ? (o.User.Email ?? o.User.Phone ?? "Walk-in") : "Walk-in",
                BranchId = o.BranchId,
                BranchName = o.Branch.Name,
                OrderType = o.OrderType,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                PaymentStatus = context.Payments.Where(p => p.OrderId == o.Id).Select(p => p.Status).FirstOrDefault() ?? "unpaid",
                CreatedAt = o.CreatedAt,
            })
            .ToListAsync();

        return Ok(new ManagedOrderPage { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ManagedOrderDetailResponse>> Get(Guid id)
    {
        var order = await context.Orders.AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Branch)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        var payment = await context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == id);
        var lines = await (from oi in context.OrderItems.AsNoTracking()
                           join inv in context.Inventory.AsNoTracking() on oi.ItemId equals inv.Id
                           where oi.OrderId == id
                           select new ManagedOrderLineItem
                           {
                               ItemName = inv.ItemName,
                               Quantity = oi.Quantity,
                               UnitPrice = oi.UnitPrice,
                           }).ToListAsync();

        return Ok(new ManagedOrderDetailResponse
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerEmail = order.User?.Email ?? order.User?.Phone ?? "Walk-in",
            BranchId = order.BranchId,
            BranchName = order.Branch.Name,
            OrderType = order.OrderType,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            DiscountAmount = order.DiscountAmount,
            DeliveryFee = order.DeliveryFee,
            DeliveryAddress = order.DeliveryAddress,
            PaymentStatus = payment?.Status ?? "unpaid",
            PaymentRef = payment?.ProviderRef,
            CreatedAt = order.CreatedAt,
            Items = lines,
        });
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> AdvanceStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        var target = request.Status?.Trim().ToLower() ?? "";
        if (!NextStatus.TryGetValue(order.Status, out var allowedNext))
            return Conflict(new { message = $"An order that is '{order.Status}' can't be advanced here." });
        if (target != allowedNext)
            return BadRequest(new { message = $"From '{order.Status}' the only next status is '{allowedNext}'." });

        var now = DateTime.UtcNow;
        order.Status = target;
        order.UpdatedAt = now;
        salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, target, now));
        await context.SaveChangesAsync();

        return Ok(new { order.Id, order.Status });
    }

    // POST /api/orders/manage/in-store-sale
    // Staff ring up a walk-in sale at the counter: the order is created already paid and
    // 'completed' (channel 'in_store'), so it skips the fulfilment chain entirely. Stock is
    // deducted and the same sales events (placed + payment + status) fire so it lands in
    // analytics like any sale. With no CustomerId the sale is booked against the system
    // "Walk-in" customer; an attached real customer earns loyalty and gets it in their history.
    [HttpPost("in-store-sale")]
    public async Task<IActionResult> InStoreSale([FromBody] InStoreSaleRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "A sale must contain at least one item." });

        // Branch: staff/managers sell at their own branch; admins (no branch) must name one.
        var branchId = IsAdmin ? request.BranchId ?? CallerBranch() : CallerBranch();
        if (branchId is null)
            return BadRequest(new
            {
                message = IsAdmin ? "branchId is required." : "Your account is not assigned to a branch."
            });
        if (GuardBranch(branchId.Value) is { } denied) return denied;

        // Attribute to an existing customer (loyalty/history) or the system Walk-in account.
        var attachedCustomer = request.CustomerId is { } cid
            ? await context.Users.FirstOrDefaultAsync(u => u.Id == cid && u.Role == "customer")
            : null;
        if (request.CustomerId is not null && attachedCustomer is null)
            return NotFound(new { message = "Attached customer not found." });
        Guid? userId = attachedCustomer?.Id;   // null = anonymous walk-in (no customer)

        var method = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "cash" : request.PaymentMethod.Trim().ToLower();
        var config = await settings.GetAsync();
        var now = DateTime.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var requestedIds = request.Items.Select(i => i.ItemId).ToList();
            // Lock the rows FOR UPDATE before checking/deducting stock (see OrdersController.PlaceOrder)
            // so a till sale can't oversell against a concurrent online order for the same item.
            var lockedItems = await InventoryLock.ForUpdateAsync(context, requestedIds);
            var inventoryItems = lockedItems.Where(i => i.BranchId == branchId.Value).ToList();

            var missingIds = requestedIds.Except(inventoryItems.Select(i => i.Id)).ToList();
            if (missingIds.Count > 0)
                return NotFound(new { message = "One or more items not found in this branch.", missingIds });

            // The in-store till sells regular retail goods only — bulk is members-only wholesale.
            var bulk = inventoryItems.Where(i => i.IsBulkOnly).Select(i => new { i.Id, i.ItemName }).ToList();
            if (bulk.Count > 0)
                return BadRequest(new { message = "Bulk items can't be sold at the in-store till.", bulk });

            var insufficientStock = request.Items
                .Join(inventoryItems, r => r.ItemId, i => i.Id, (r, i) => new { r.Quantity, Item = i })
                .Where(x => x.Quantity <= 0 || x.Item.StockQuantity < x.Quantity)
                .Select(x => new { x.Item.Id, x.Item.ItemName, x.Item.StockQuantity })
                .ToList();
            if (insufficientStock.Count > 0)
                return Conflict(new { message = "Insufficient stock for one or more items.", insufficientStock });

            // Deduct stock and total up.
            decimal subtotal = 0;
            var lineItems = new List<(Inventory Inv, int Qty)>();
            foreach (var requestItem in request.Items)
            {
                var inv = inventoryItems.First(i => i.Id == requestItem.ItemId);
                inv.StockQuantity -= requestItem.Quantity;
                inv.UpdatedAt = now;
                subtotal += inv.Price * requestItem.Quantity;
                lineItems.Add((inv, requestItem.Quantity));
            }

            var isMember = attachedCustomer is { IsMember: true };
            var discount = isMember ? Math.Round(subtotal * config.MemberDiscountRate, 2) : 0m;
            var goodsTotal = subtotal - discount;   // no delivery fee for an in-store sale

            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BranchId = branchId.Value,
                OrderType = "pickup",
                Channel = "in_store",
                Status = "completed",
                TotalAmount = goodsTotal,
                DiscountAmount = discount,
                DeliveryFee = 0m,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Orders.Add(order);

            var eventLines = new List<OrderLineDto>();
            var rn = 0;
            foreach (var (inv, qty) in lineItems)
            {
                rn++;
                var lineId = Guid.NewGuid();
                context.OrderItems.Add(new OrderItem
                {
                    Id = lineId,
                    OrderId = order.Id,
                    ItemId = inv.Id,
                    Quantity = qty,
                    UnitPrice = inv.Price
                });
                eventLines.Add(new OrderLineDto(lineId, inv.Id, inv.ItemName, inv.IsBulkOnly, qty, inv.Price, rn));
            }

            // Paid at the till — record the completed payment immediately.
            context.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                TransactionUuid = $"instore-{Guid.NewGuid()}",
                Amount = goodsTotal,
                Status = "completed",
                ProviderRef = $"instore:{method}",
                CreatedAt = now,
                UpdatedAt = now,
            });

            // Loyalty accrues only for an attached real customer (walk-in earns nothing).
            if (attachedCustomer is not null)
            {
                attachedCustomer.LoyaltyPoints += (int)Math.Floor(goodsTotal);
                attachedCustomer.UpdatedAt = now;
            }

            await context.SaveChangesAsync();   // populates the DB-generated order.OrderNumber

            var branchName = await context.Branches
                .Where(b => b.Id == branchId.Value).Select(b => b.Name).FirstAsync();

            // Born completed + paid: emit placed, payment-completed and status-completed together
            // so the warehouse sees a finished, collected sale in one go.
            salesEvents.OrderPlaced(new OrderPlacedEvent(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                OccurredAt: now,
                BranchName: branchName,
                OrderType: order.OrderType,
                Channel: order.Channel,
                IsMember: isMember,
                CustomerId: userId,
                OrderDiscount: order.DiscountAmount,
                OrderDeliveryFee: 0m,
                Lines: eventLines));
            salesEvents.PaymentStatusChanged(new PaymentStatusChangedEvent(order.Id, "completed", now));
            salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, "completed", now));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Stock-change events after commit (fire-and-forget; no-op unless Kafka is configured).
            foreach (var (inv, qty) in lineItems)
            {
                stockEvents.Publish(new StockChangedEvent(
                    ItemId: inv.Id,
                    BranchId: inv.BranchId,
                    ItemName: inv.ItemName,
                    OldQuantity: inv.StockQuantity + qty,
                    NewQuantity: inv.StockQuantity,
                    Reason: "in_store_sale",
                    OccurredAt: now));
            }

            return Ok(new { order.Id, order.OrderNumber, order.Status, order.Channel, order.TotalAmount });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET /api/orders/manage/customers?q=
    // Look up shoppers (customer/shareholder) by email or phone so staff can optionally attach one
    // to an in-store sale (to credit loyalty / put it in their history). Not branch-scoped —
    // customers aren't tied to a branch. Returns at most 10 matches.
    [HttpGet("customers")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var term = $"%{q.Trim().ToLower()}%";
        var now = DateTime.UtcNow;
        var matches = await context.Users.AsNoTracking()
            .Where(u => (u.Role == "customer" || u.Role == "shareholder") && !u.IsBanned
                        && (EF.Functions.ILike(u.Email!, term) || EF.Functions.ILike(u.Phone!, term)))
            .OrderBy(u => u.Email)
            .Take(10)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Phone,
                IsMember = u.Role == "shareholder" || (u.MemberUntil.HasValue && u.MemberUntil.Value > now),
                u.LoyaltyPoints,
            })
            .ToListAsync();

        return Ok(matches);
    }

    // POST /api/orders/manage/customers  { phone }
    // Quick-create a "guest" customer at the till from just a phone number (no app login until they
    // claim the account), so a first-time walk-in can start earning loyalty. Idempotent: if a user
    // with that phone already exists it's returned, so repeat visits don't create duplicates.
    [HttpPost("customers")]
    public async Task<IActionResult> CreateGuestCustomer([FromBody] GuestCustomerRequest request)
    {
        if (!PhoneNumber.TryNormalize(request.Phone, out var phone))
            return BadRequest(new { message = "Enter a valid phone number (up to 10 digits)." });

        var now = DateTime.UtcNow;
        var user = await context.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), Phone = phone, Role = "customer", CreatedAt = now, UpdatedAt = now };
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        return Ok(new
        {
            user.Id,
            user.Email,
            user.Phone,
            IsMember = user.Role == "shareholder" || (user.MemberUntil.HasValue && user.MemberUntil.Value > now),
            user.LoyaltyPoints,
        });
    }

    [HttpPost("{id:guid}/record-payment")]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        if (order.Status != "pending")
            return Conflict(new { message = $"Order is '{order.Status}'; only a pending order can be marked paid." });

        var payment = await context.Payments.FirstOrDefaultAsync(p => p.OrderId == id);
        if (payment is { Status: "completed" })
            return Conflict(new { message = "Order is already paid." });

        var method = string.IsNullOrWhiteSpace(request.Method) ? "cash" : request.Method.Trim().ToLower();
        var now = DateTime.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            if (payment is null)
            {
                payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    TransactionUuid = $"manual-{Guid.NewGuid()}",
                    Amount = order.TotalAmount,
                    Status = "completed",
                    ProviderRef = $"manual:{method}",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.Payments.Add(payment);
            }
            else
            {
                payment.Status = "completed";
                payment.Amount = order.TotalAmount;
                payment.ProviderRef = $"manual:{method}";
                payment.UpdatedAt = now;
            }

            order.Status = "confirmed";
            order.UpdatedAt = now;

            // Mirror eSewa success: credit loyalty once paid (goods value, excl delivery).
            var goodsTotal = order.TotalAmount - order.DeliveryFee;
            var user = order.UserId is { } uid ? await context.Users.FindAsync(uid) : null;
            if (user is not null)
            {
                user.LoyaltyPoints += (int)Math.Floor(goodsTotal);
                user.UpdatedAt = now;
            }

            salesEvents.PaymentStatusChanged(new PaymentStatusChangedEvent(order.Id, "completed", now));
            salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, "confirmed", now));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return Ok(new { order.Id, order.Status, PaymentStatus = "completed" });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        if (order.Status is "completed" or "cancelled")
            return Conflict(new { message = $"A '{order.Status}' order can't be cancelled." });

        // A paid order is a refund when cancelled — only allow it before fulfilment begins
        // (still pending/confirmed). Once it's being processed, it must be handled manually.
        var isPaid = await context.Payments.AnyAsync(p => p.OrderId == id && p.Status == "completed");
        if (isPaid && order.Status is not ("pending" or "confirmed"))
            return Conflict(new { message = "A paid order can only be cancelled before it is processed." });

        await cancellation.CancelAsync(order);

        return Ok(new { order.Id, order.Status });
    }
}
