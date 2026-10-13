namespace Pos.Domain.TableSessions;

public enum TableSessionAction
{
    OpenTable,
    SubmitOrder,
    ChangeTable,
    RequestPayment,
    CancelPaymentRequest,
    StartPayment,
    PaymentFailed,
    CancelPayment,
    ConfirmPartialPayment,
    CompletePayment,
    ReverseAllConfirmedPaymentLines,
    CancelBill,
    CloseTable
}
